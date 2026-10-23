using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectAbyss.Core.Items;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Run;
using Xunit;

namespace ProjectAbyss.Core.Tests
{
    public class PersistenceTests
    {
        private sealed class Payload
        {
            public int Value { get; set; }
        }

        [Fact]
        public void EnvelopeRoundTripsAndDetectsTampering()
        {
            using var dir = new TempDir();
            string path = Path.Combine(dir.Path, "x.json");
            EnvelopeIO.Save(path, "test", 1, 1, new Payload { Value = 7 });
            var ok = EnvelopeIO.Load<Payload>(path, "test", 1);
            Assert.Equal(LoadStatus.Ok, ok.Status);
            Assert.Equal(7, ok.Value!.Value);

            var env = System.Text.Json.JsonSerializer.Deserialize<SaveEnvelope>(File.ReadAllText(path), Data.GameData.JsonOptions)!;
            Assert.Contains("7", env.Payload);
            env.Payload = env.Payload.Replace("7", "9");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(env, Data.GameData.JsonOptions));
            var tampered = EnvelopeIO.Load<Payload>(path, "test", 1);
            Assert.Equal(LoadStatus.Corrupt, tampered.Status);
            Assert.Equal(LoadStatus.WrongKind, EnvelopeIO.Load<Payload>(path + ".bak", "other", 1).Status == LoadStatus.Missing ? LoadStatus.WrongKind : EnvelopeIO.Load<Payload>(path, "other", 1).Status);
        }

        [Fact]
        public void CorruptPrimaryFallsBackToBackup()
        {
            using var dir = new TempDir();
            string path = Path.Combine(dir.Path, "x.json");
            EnvelopeIO.Save(path, "test", 1, 1, new Payload { Value = 1 });
            EnvelopeIO.Save(path, "test", 1, 2, new Payload { Value = 2 });
            File.WriteAllText(path, "{ broken");
            var r = EnvelopeIO.Load<Payload>(path, "test", 1);
            Assert.Equal(LoadStatus.RecoveredFromBackup, r.Status);
            Assert.Equal(1, r.Value!.Value);
        }

        [Fact]
        public void NewerSchemaIsRefusedNotMisread()
        {
            using var dir = new TempDir();
            string path = Path.Combine(dir.Path, "x.json");
            EnvelopeIO.Save(path, "test", 5, 1, new Payload { Value = 1 });
            Assert.Equal(LoadStatus.UnsupportedVersion, EnvelopeIO.Load<Payload>(path, "test", 1).Status);
        }

        [Fact]
        public void JournalDiscardsAndRepairsTornTail()
        {
            using var dir = new TempDir();
            string path = Path.Combine(dir.Path, "journal.log");
            var j = new TransactionJournal(path);
            j.Append(new RunTransaction { TxId = "a", Kind = TxKind.GrantGold, ParticipantId = "p", Amount = 5 });
            j.Append(new RunTransaction { TxId = "b", Kind = TxKind.GrantGold, ParticipantId = "p", Amount = 6 });
            File.AppendAllText(path, "{\"seq\":3,\"sha\":\"dead\",\"tx\":{\"txId\":\"c\""); // crash mid-write
            var reopened = new TransactionJournal(path);
            Assert.True(reopened.RepairedOnOpen);
            Assert.Equal(2, reopened.LastSequence);
            reopened.Append(new RunTransaction { TxId = "d", Kind = TxKind.GrantGold, ParticipantId = "p", Amount = 1 });
            var all = reopened.ReadAll(out bool damaged);
            Assert.False(damaged);
            Assert.Equal(new[] { "a", "b", "d" }, all.Select(e => e.Tx.TxId));
        }

        private static RunState NewRun(params string[] ids) =>
            RunController.CreateRun(TestData.Data, "r1", 99, "normal", ids.Select(id => (id, id, "warrior", "")), developer: false);

        [Fact]
        public void TransactionsAreIdempotent()
        {
            var run = NewRun("a");
            var proc = new TransactionProcessor(TestData.Data);
            var tx = new RunTransaction { TxId = "gold1", Kind = TxKind.GrantGold, ParticipantId = "a", Amount = 50 };
            Assert.Equal(TxOutcome.Applied, proc.Apply(run, tx).Outcome);
            Assert.Equal(TxOutcome.Duplicate, proc.Apply(run, tx).Outcome);
            Assert.Equal(50, run.Get("a").Gold);
            Assert.Equal(TxOutcome.Rejected, proc.Apply(run, new RunTransaction { TxId = "spend", Kind = TxKind.SpendGold, ParticipantId = "a", Amount = 51 }).Outcome);
            Assert.Equal(TxOutcome.Rejected, proc.Apply(run, new RunTransaction { TxId = "neg", Kind = TxKind.GrantGold, ParticipantId = "a", Amount = -5 }).Outcome);
            Assert.Equal(TxOutcome.Rejected, proc.Apply(run, new RunTransaction { TxId = "x", Kind = TxKind.GrantGold, ParticipantId = "stranger", Amount = 5 }).Outcome);
        }

        [Fact]
        public void TradingMovesItemWithoutDuplication()
        {
            var run = NewRun("a", "b");
            var proc = new TransactionProcessor(TestData.Data);
            var item = new ItemInstance { InstanceId = run.NewItemId(), DefId = "rusty_sword" };
            Assert.True(proc.Apply(run, new RunTransaction { TxId = "g", Kind = TxKind.GrantItem, ParticipantId = "a", Item = item }).Succeeded);
            // the same instance cannot be granted twice under another tx id
            Assert.Equal(TxOutcome.Rejected, proc.Apply(run, new RunTransaction { TxId = "g2", Kind = TxKind.GrantItem, ParticipantId = "b", Item = item.Clone() }).Outcome);
            var trade = new RunTransaction { TxId = "t", Kind = TxKind.TradeItem, ParticipantId = "a", OtherParticipantId = "b", ItemId = item.InstanceId };
            Assert.True(proc.Apply(run, trade).Succeeded);
            Assert.Equal(TxOutcome.Duplicate, proc.Apply(run, trade).Outcome);
            Assert.False(run.Get("a").Inventory.Contains(item.InstanceId));
            Assert.True(run.Get("b").Inventory.Contains(item.InstanceId));
            // selling an item you no longer own is rejected
            Assert.Equal(TxOutcome.Rejected, proc.Apply(run, new RunTransaction { TxId = "s", Kind = TxKind.SellItem, ParticipantId = "a", ItemId = item.InstanceId }).Outcome);
        }

        [Fact]
        public void FullInventoryKeepsRewardsAsPending()
        {
            var run = NewRun("a");
            var proc = new TransactionProcessor(TestData.Data);
            for (int i = 0; i < 26; i++)
                Assert.True(proc.Apply(run, new RunTransaction { TxId = "g" + i, Kind = TxKind.GrantItem, ParticipantId = "a", Item = new ItemInstance { InstanceId = run.NewItemId(), DefId = "leather_cap" } }).Succeeded);
            var p = run.Get("a");
            Assert.Equal(0, p.Inventory.FreeSlots);
            Assert.Equal(2, p.PendingRewards.Count);
            var sell = p.Inventory.Items.First();
            Assert.True(proc.Apply(run, new RunTransaction { TxId = "sell", Kind = TxKind.SellItem, ParticipantId = "a", ItemId = sell.InstanceId }).Succeeded);
            Assert.True(proc.Apply(run, new RunTransaction { TxId = "claim", Kind = TxKind.ClaimPendingReward, ParticipantId = "a", ItemId = p.PendingRewards[0].InstanceId }).Succeeded);
            Assert.Single(p.PendingRewards);
        }

        [Fact]
        public void ShopReviveRequiresCoveredConfirmedContributions()
        {
            var run = NewRun("a", "b");
            var proc = new TransactionProcessor(TestData.Data);
            run.Get("a").Gold = 300;
            run.Get("b").Gold = 100;
            run.Get("b").Lives = 0;
            run.Get("b").Eliminated = true;
            var tooLittle = new RunTransaction { TxId = "r1", Kind = TxKind.ShopRevive, ParticipantId = "b", Amount = 500, Contributions = new() { ["a"] = 300, ["b"] = 100 } };
            Assert.Equal(TxOutcome.Rejected, proc.Apply(run, tooLittle).Outcome);
            var ok = new RunTransaction { TxId = "r2", Kind = TxKind.ShopRevive, ParticipantId = "b", Amount = 350, Contributions = new() { ["a"] = 300, ["b"] = 50 } };
            Assert.True(proc.Apply(run, ok).Succeeded);
            Assert.Equal(1, run.Get("b").Lives);
            Assert.False(run.Get("b").Eliminated);
            Assert.Equal(0, run.Get("a").Gold);
            Assert.Equal(50, run.Get("b").Gold);
        }

        [Fact]
        public void XpCurveYieldsNineteenLevelUps()
        {
            var run = NewRun("a");
            var proc = new TransactionProcessor(TestData.Data);
            var p = run.Get("a");
            proc.GrantXp(p, 10_000_000);
            Assert.Equal(20, p.Level);
            Assert.Equal(19, p.UnspentSkillPoints);
        }

        [Fact]
        public void RecoveryReplaysJournalAndReconcilesWorld()
        {
            using var dir = new TempDir();
            var store = new RunStore(dir.Path);
            var run = NewRun("a");
            var controller = new RunController(TestData.Data, run, store);
            var world = controller.BeginArena();
            for (int i = 0; i < 200; i++)
                controller.Step(new Dictionary<int, Simulation.PlayerInput>());
            controller.SaveRecoverySnapshot();
            // a kill that was journaled after the last snapshot (crash before the next snapshot)
            var enemy = world.State.Enemies.First(e => e.Hp > 0);
            controller.Ledger.Commit(new RunTransaction { TxId = RunTransaction.KillTxId(run.RunId, run.ArenaAttempt, enemy.Id, "a"), Kind = TxKind.KillReward, ParticipantId = "a", Amount = 7, Xp = 3 });
            long goldBefore = run.Get("a").Gold;

            var recovered = new RunStore(dir.Path).Load(new TransactionProcessor(TestData.Data))!;
            Assert.NotNull(recovered.World);
            Assert.Equal(1, recovered.ReplayedTransactions);
            Assert.Equal(goldBefore, recovered.Run.Get("a").Gold);
            Assert.Equal(0, recovered.World!.Enemies.First(e => e.Id == enemy.Id).Hp);

            // replaying again (second crash before checkpoint) does not grant twice
            var again = new RunStore(dir.Path).Load(new TransactionProcessor(TestData.Data))!;
            Assert.Equal(goldBefore, again.Run.Get("a").Gold);
        }
    }
}
