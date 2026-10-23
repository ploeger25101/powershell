using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Randomness;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectAbyss.Core.Tests
{
    public class CrashRecoveryTests
    {
        private readonly ITestOutputHelper output;

        public CrashRecoveryTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Theory]
        [InlineData(1UL)]
        [InlineData(2UL)]
        [InlineData(3UL)]
        public void RepeatedHostCrashesNeverDuplicateOrLoseConfirmedRewards(ulong seed)
        {
            using var dir = new TempDir();
            var data = TestData.Data;
            var run = RunController.CreateRun(data, "crash" + seed, seed, "normal", new[] { ("a", "A", "warrior", "warrior_axe"), ("b", "B", "warrior", "warrior_mace") }, developer: false);
            var confirmed = new Dictionary<string, RunTransaction>();
            var chaos = new DeterministicRng(seed, 0xC4A05);
            int crashes = 0;
            RunController? controller = null;

            RunController Attach(RunState state)
            {
                var c = new RunController(data, state, new RunStore(dir.Path));
                c.Ledger.Committed += tx => confirmed[tx.TxId] = tx;
                return c;
            }

            controller = Attach(run);
            controller.BeginArena();
            var bots = new Dictionary<string, AutoPilot>();
            int safety = 0;
            while (controller.Run.Status == RunStatus.Active && controller.Run.CurrentLevel <= 3 && safety++ < 200_000)
            {
                if (controller.Phase == RunPhase.BetweenArenas)
                {
                    controller.BeginArena();
                    bots.Clear();
                }
                if (controller.Phase != RunPhase.InArena)
                    break;
                var world = controller.World!;
                var inputs = new Dictionary<int, PlayerInput>();
                foreach (var p in world.State.Players)
                {
                    if (!bots.TryGetValue(p.ParticipantId, out var bot))
                        bots[p.ParticipantId] = bot = new AutoPilot((ulong)p.Id + seed);
                    inputs[p.Id] = bot.Next(world.State, p.Id);
                }
                controller.Step(inputs);

                if (chaos.Chance(0.004f))
                {
                    // simulated host crash: the process dies; sometimes in the middle of a journal write
                    crashes++;
                    if (chaos.Chance(0.3f))
                        File.AppendAllText(Path.Combine(dir.Path, "journal.log"), "{\"seq\":999999,\"sha\":\"x\",\"tx\":{");
                    var recovered = new RunStore(dir.Path).Load(new TransactionProcessor(data))!;
                    Assert.NotNull(recovered);
                    controller = Attach(recovered.Run);
                    if (recovered.World != null)
                        controller.ResumeArena(recovered.World);
                    bots.Clear();
                }
            }
            this.output.WriteLine($"seed {seed}: level {controller.Run.CurrentLevel}, crashes {crashes}, confirmed tx {confirmed.Count}");
            Assert.True(crashes > 0, "test did not exercise any crash");

            // every confirmed transaction is reflected exactly once
            var final = controller.Run;
            foreach (var p in final.Participants)
            {
                long expectedGold = confirmed.Values.Where(t => t.ParticipantId == p.ParticipantId && t.Kind is TxKind.KillReward or TxKind.GrantGold).Sum(t => t.Amount);
                Assert.Equal(expectedGold, p.Gold);
                int expectedLivesLost = confirmed.Values.Count(t => t.ParticipantId == p.ParticipantId && t.Kind == TxKind.LoseLife);
                Assert.Equal(3 - expectedLivesLost, p.Lives);
            }
            Assert.True(confirmed.Keys.All(final.AppliedTransactions.Contains));
            var ids = final.Participants.SelectMany(p => p.Inventory.Items.Concat(p.PendingRewards).Concat(p.RunChest.Items)).Select(i => i.InstanceId).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void PactNeedsUnanimityWaitsForOfflineVotersAndIsSingleUse()
        {
            using var dir = new TempDir();
            var data = TestData.Data;
            var run = RunController.CreateRun(data, "pact", 5, "normal", new[] { ("a", "A", "warrior", ""), ("b", "B", "warrior", ""), ("c", "C", "warrior", "") }, developer: false);
            run.CurrentLevel = 5;
            foreach (var p in run.Participants)
                p.Gold = 100;
            var controller = new RunController(data, run, new RunStore(dir.Path));
            var world = controller.BeginArena();
            // wipe the party
            foreach (var p in world.State.Players)
            {
                p.Lives = 1;
                p.Hp = 0.1f;
            }
            for (int i = 0; i < 5000 && controller.Phase == RunPhase.InArena; i++)
                controller.Step(new Dictionary<int, PlayerInput>());
            Assert.Equal(RunPhase.PactVote, controller.Phase);

            Assert.True(controller.VotePact("a", true).Succeeded);
            Assert.True(controller.VotePact("b", true).Succeeded);
            Assert.Equal(new[] { "c" }, controller.PendingPactVoters());

            // host restarts while "c" is offline: the open vote survives
            var reloaded = new RunStore(dir.Path).Load(new TransactionProcessor(data))!;
            var c2 = new RunController(data, reloaded.Run, new RunStore(dir.Path));
            Assert.Equal(RunPhase.PactVote, c2.Phase);
            Assert.Equal(new[] { "c" }, c2.PendingPactVoters());
            Assert.Equal(TxOutcome.Rejected, c2.VotePact("stranger", true).Outcome);

            Assert.True(c2.VotePact("c", true).Succeeded);
            Assert.True(c2.Run.Pact.Used);
            Assert.All(c2.Run.Participants, p => Assert.Equal(1, p.Lives));
            Assert.All(c2.Run.Participants, p => Assert.Equal(0, p.Gold));
            var retry = c2.BeginArena();
            Assert.True(retry.State.PactActive);
            retry.Step();
            var boss = retry.State.Enemies.Single(e => e.IsBoss);
            Assert.Equal(boss.MaxHp, boss.Hp);
            Assert.Contains("verdant_wrath", retry.AvailableBossAttacks(boss));

            // second wipe: no second pact, run ends
            foreach (var p in retry.State.Players)
                p.Hp = 0.1f;
            for (int i = 0; i < 5000 && c2.Phase == RunPhase.InArena; i++)
                c2.Step(new Dictionary<int, PlayerInput>());
            Assert.Equal(RunPhase.Finished, c2.Phase);
            Assert.Equal(RunStatus.Lost, c2.Run.Status);

            // reloading cannot resurrect the pact
            var again = new RunStore(dir.Path).Load(new TransactionProcessor(data))!;
            Assert.True(again.Run.Pact.Used);
            Assert.Equal(RunStatus.Lost, again.Run.Status);
        }

        [Fact]
        public void EveryLevelCanStartAnArenaEvenWithoutBiomeContent()
        {
            var data = TestData.Data;
            for (int level = 1; level <= RunPlan.TotalLevels; level++)
            {
                var run = RunController.CreateRun(data, "lvl" + level, (ulong)level, "normal", new[] { ("a", "A", "warrior", "") }, developer: true);
                run.CurrentLevel = level;
                var c = new RunController(data, run, null);
                var world = c.BeginArena();
                for (int i = 0; i < 120; i++)
                    c.Step(new Dictionary<int, PlayerInput>());
                Assert.Equal(level, world.State.Level);
                Assert.True(world.State.Enemies.Count > 0 || world.State.Encounter.Status != EncounterStatus.InProgress, $"level {level} has no enemies");
            }
        }

        [Fact]
        public void SingleNoVoteEndsTheRun()
        {
            var data = TestData.Data;
            var run = RunController.CreateRun(data, "pact2", 5, "normal", new[] { ("a", "A", "warrior", ""), ("b", "B", "warrior", "") }, developer: false);
            run.Pact.VoteOpen = true;
            run.Pact.BossLevel = 5;
            var c = new RunController(data, run, null);
            Assert.Equal(RunPhase.PactVote, c.Phase);
            c.VotePact("a", true);
            c.VotePact("b", false);
            Assert.Equal(RunPhase.Finished, c.Phase);
            Assert.False(c.Run.Pact.Used);
        }
    }
}
