using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Items;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Randomness;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Run
{
    public enum RunPhase
    {
        BetweenArenas,
        InArena,
        PactVote,
        Finished,
    }

    /// <summary>
    /// Host-only orchestration of a run: builds arenas, converts simulation events into journaled transactions,
    /// writes checkpoints at safe transitions and crash snapshots during combat.
    /// </summary>
    public sealed class RunController
    {
        public const float RecoverySnapshotIntervalSeconds = 3f;

        private readonly GameData data;
        private readonly RunStore? store;
        private readonly TransactionProcessor processor;
        private readonly LootGenerator loot;
        private DeterministicRng lootRng;
        private bool snapshotRequested;
        private int lastSnapshotTick;

        public RunController(GameData data, RunState run, RunStore? store)
        {
            this.data = data;
            this.store = store;
            this.processor = new TransactionProcessor(data);
            this.loot = new LootGenerator(data);
            this.Ledger = new RunLedger(run, this.processor, store?.Journal);
            this.Ledger.Committed += this.OnCommitted;
            this.lootRng = DeterministicRng.FromState(run.LootRng);
            this.Phase = run.Status != RunStatus.Active ? RunPhase.Finished : run.Pact.VoteOpen ? RunPhase.PactVote : RunPhase.BetweenArenas;
        }

        public RunLedger Ledger { get; }
        public RunState Run => this.Ledger.Run;
        public CombatWorld? World { get; private set; }
        public RunPhase Phase { get; private set; }
        public TransactionProcessor Processor => this.processor;

        /// <summary>Blueprints per participant (from their own profiles); only affects their personal loot pool.</summary>
        public Dictionary<string, ISet<string>> Blueprints { get; } = new();

        public event Action<string>? Log;

        public static RunState CreateRun(GameData data, string runId, ulong seed, string difficultyId, IEnumerable<(string id, string name, string classId, string weapon)> players, bool developer)
        {
            var rng = new DeterministicRng(seed, 0xB055);
            var run = new RunState
            {
                RunId = runId,
                Seed = seed,
                DifficultyId = difficultyId,
                CreatedUtc = DateTime.UtcNow,
                IsDeveloperRun = developer,
                ContentHash = data.ContentHash,
                LootRng = new DeterministicRng(seed, 0x1007).SaveState(),
            };
            for (int biome = 1; biome <= 5; biome++)
            {
                var options = data.Bosses.Values.Where(b => b.Biome == biome).OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
                if (options.Count > 0)
                    run.BossSelection[biome] = options[rng.NextInt(0, options.Count)].Id;
            }
            foreach (var (id, name, classId, weapon) in players)
            {
                var cls = data.Classes[classId];
                var p = new RunParticipant
                {
                    ParticipantId = id,
                    DisplayName = name,
                    ClassId = classId,
                    WeaponTypeId = string.IsNullOrEmpty(weapon) ? cls.WeaponTypes[0] : weapon,
                    Lives = data.Balance.Lives.StartLives,
                    PotionCharges = data.Balance.Potion.StartCharges,
                    EquippedAbilities = cls.StarterAbilities.Take(2).ToList(),
                    Ultimate = cls.BaseUltimate,
                    Resource = cls.Resource.Start,
                    Inventory = new Inventory(data.Balance.Economy.InventorySlots),
                    RunChest = new Inventory(data.Balance.Economy.RunChestSlots),
                };
                run.Participants.Add(p);
                run.OriginalParticipants.Add(id);
            }
            if (run.Participants.Count is < 1 or > 4)
                throw new ArgumentException("A run needs 1-4 participants.");
            return run;
        }

        /// <summary>Resume an arena from a crash snapshot (host restart).</summary>
        public void ResumeArena(WorldState world)
        {
            this.World = new CombatWorld(this.data, world);
            this.Phase = RunPhase.InArena;
            this.lastSnapshotTick = world.Tick;
            this.Log?.Invoke($"resumed arena at tick {world.Tick}");
        }

        public CombatWorld BeginArena(ISet<string>? connectedParticipants = null)
        {
            if (this.Phase != RunPhase.BetweenArenas)
                throw new InvalidOperationException($"Cannot start an arena in phase {this.Phase}.");
            var run = this.Run;
            int level = run.CurrentLevel;
            int biome = RunPlan.BiomeOf(level);
            run.ArenaAttempt++;
            ulong arenaSeed = DeterministicRng.Mix(run.Seed, (ulong)(level * 1000 + run.ArenaAttempt));
            var fighters = run.Participants.Where(p => !p.Eliminated).ToList();
            int playerCount = Math.Max(1, fighters.Count);

            var setup = new ArenaSetup { RunId = run.RunId, Seed = arenaSeed, Level = level, DifficultyId = run.DifficultyId, PactActive = run.Pact.Used && run.Pact.BossLevel == level };
            if (RunPlan.IsBossLevel(level) && run.BossSelection.TryGetValue(biome, out string? bossId))
            {
                setup.ArenaTemplateId = this.data.Bosses[bossId].ArenaTemplate;
                setup.Encounter = EncounterDirector.BossEncounter(bossId, playerCount);
            }
            else
            {
                var templates = this.data.Arenas.Values.Where(a => a.Biome == biome && !a.IsBossArena && !a.IsFallback).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
                var rng = new DeterministicRng(arenaSeed, 0xE7C);
                setup.ArenaTemplateId = templates.Count > 0 ? templates[rng.NextInt(0, templates.Count)].Id : this.data.Arenas.Values.First(a => a.IsFallback && !a.IsBossArena).Id;
                var kinds = new[] { EncounterKind.Waves, EncounterKind.Waves, EncounterKind.Survival, EncounterKind.EliteHunt };
                var kind = level <= 1 ? EncounterKind.Waves : kinds[rng.NextInt(0, kinds.Length)];
                setup.Encounter = new EncounterDirector(this.data).Build(kind, biome, level, run.DifficultyId, playerCount, false, rng);
            }
            foreach (var p in run.Participants)
            {
                setup.Players.Add(new PlayerLoadout
                {
                    ParticipantId = p.ParticipantId,
                    DisplayName = p.DisplayName,
                    ClassId = p.ClassId,
                    WeaponTypeId = p.WeaponTypeId,
                    Level = p.Level,
                    Hp = p.Hp,
                    Lives = p.Lives,
                    Eliminated = p.Eliminated,
                    Resource = p.Resource,
                    PotionCharges = p.PotionCharges,
                    EquippedAbilities = p.EquippedAbilities.ToList(),
                    Ultimate = p.Ultimate,
                    RemainingCooldowns = new Dictionary<string, float>(p.RemainingCooldowns),
                    Connected = connectedParticipants == null || connectedParticipants.Contains(p.ParticipantId),
                });
            }
            this.World = new CombatWorld(this.data, WorldFactory.Create(this.data, setup));
            this.Phase = RunPhase.InArena;
            this.store?.SaveCheckpoint(run);
            this.lastSnapshotTick = this.World.Tick;
            this.Log?.Invoke($"level {level} arena {setup.ArenaTemplateId} attempt {run.ArenaAttempt} ({setup.Encounter.Kind})");
            return this.World;
        }

        /// <summary>Advance the arena one tick and process its events. Returns the events of that tick.</summary>
        public IReadOnlyList<SimEvent> Step(IReadOnlyDictionary<int, PlayerInput> inputs)
        {
            var world = this.World ?? throw new InvalidOperationException("No active arena.");
            world.Step(inputs);
            foreach (var e in world.Events)
                this.HandleEvent(world, e);

            if (this.Phase == RunPhase.InArena)
            {
                var status = world.State.Encounter.Status;
                if (status == EncounterStatus.Cleared)
                    this.OnArenaCleared(world);
                else if (status == EncounterStatus.Failed)
                    this.OnPartyDefeated(world);
            }
            if (this.Phase == RunPhase.InArena && this.store != null
                && (this.snapshotRequested || world.Tick - this.lastSnapshotTick >= SimTime.Ticks(RecoverySnapshotIntervalSeconds)))
            {
                this.SaveRecoverySnapshot();
            }
            return world.Events;
        }

        public void SaveRecoverySnapshot()
        {
            if (this.World == null || this.store == null)
                return;
            this.Run.LootRng = this.lootRng.SaveState();
            this.store.SaveRecovery(this.Run, this.World.CaptureState());
            this.lastSnapshotTick = this.World.Tick;
            this.snapshotRequested = false;
        }

        private void OnCommitted(RunTransaction tx)
        {
            if (tx.Kind is TxKind.LoseLife or TxKind.PactConsume or TxKind.ShopRevive or TxKind.TradeItem or TxKind.SellItem)
                this.snapshotRequested = true;
        }

        private void HandleEvent(CombatWorld world, SimEvent e)
        {
            var run = this.Run;
            switch (e.Type)
            {
                case SimEventType.EnemyKilled:
                {
                    bool boss = e.Flag;
                    bool elite = e.Amount > 0;
                    foreach (var p in run.Participants)
                    {
                        // personal rewards for everyone in the run (eliminated players keep their state for later revival)
                        var item = this.loot.RollDrop(run, this.lootRng, p, elite, boss, this.Blueprints.GetValueOrDefault(p.ParticipantId) ?? new HashSet<string>());
                        this.Ledger.Commit(new RunTransaction
                        {
                            TxId = RunTransaction.KillTxId(run.RunId, run.ArenaAttempt, e.TargetId, p.ParticipantId),
                            Kind = TxKind.KillReward,
                            ParticipantId = p.ParticipantId,
                            Amount = e.IntValue2,
                            Xp = e.IntValue,
                            Item = item,
                            Key = e.Key,
                        });
                    }
                    break;
                }
                case SimEventType.LifeLost:
                {
                    var player = world.FindPlayer(e.TargetId);
                    if (player == null)
                        break;
                    // keyed by the remaining life count: one confirmed loss per life, never applied twice
                    this.Ledger.Commit(new RunTransaction
                    {
                        TxId = $"life:{run.RunId}:{player.ParticipantId}:{e.IntValue}",
                        Kind = TxKind.LoseLife,
                        ParticipantId = player.ParticipantId,
                    });
                    break;
                }
            }
        }

        private void SyncParticipantsFromWorld(CombatWorld world)
        {
            foreach (var wp in world.State.Players)
            {
                var rp = this.Run.Find(wp.ParticipantId);
                if (rp == null)
                    continue;
                rp.Hp = wp.LifeState == LifeState.Alive ? wp.Hp : (float?)null;
                rp.Resource = wp.Resource;
                rp.PotionCharges = wp.PotionCharges;
                rp.RemainingCooldowns = wp.CooldownReadyTick
                    .Where(c => c.Value > world.Tick)
                    .ToDictionary(c => c.Key, c => SimTime.Seconds(c.Value - world.Tick));
            }
        }

        private void OnArenaCleared(CombatWorld world)
        {
            var run = this.Run;
            this.SyncParticipantsFromWorld(world);
            int level = run.CurrentLevel;
            if (RunPlan.IsBossLevel(level))
                this.Ledger.Commit(new RunTransaction { TxId = $"milestone:{run.RunId}:{level}", Kind = TxKind.BossMilestone });
            if (RunPlan.IsFinalLevel(level))
            {
                this.Ledger.Commit(new RunTransaction { TxId = $"end:{run.RunId}", Kind = TxKind.EndRun, Flag = true });
                this.Phase = RunPhase.Finished;
            }
            else
            {
                this.Ledger.Commit(new RunTransaction { TxId = $"advance:{run.RunId}:{level + 1}", Kind = TxKind.AdvanceLevel, Amount = level + 1 });
                this.Phase = RunPhase.BetweenArenas;
            }
            this.Run.LootRng = this.lootRng.SaveState();
            this.store?.SaveCheckpoint(run);
            this.World = null;
            this.Log?.Invoke($"level {level} cleared");
        }

        private void OnPartyDefeated(CombatWorld world)
        {
            var run = this.Run;
            this.SyncParticipantsFromWorld(world);
            if (RunPlan.IsBossLevel(run.CurrentLevel) && !run.Pact.Used)
            {
                run.Pact.VoteOpen = true;
                run.Pact.BossLevel = run.CurrentLevel;
                run.Pact.Votes.Clear();
                this.Phase = RunPhase.PactVote;
                this.Log?.Invoke("party defeated by boss: Pact of the Last Chance vote opened");
            }
            else
            {
                this.Ledger.Commit(new RunTransaction { TxId = $"end:{run.RunId}", Kind = TxKind.EndRun, Flag = false });
                this.Phase = RunPhase.Finished;
                this.Log?.Invoke("party defeated: run lost");
            }
            this.Run.LootRng = this.lootRng.SaveState();
            this.store?.SaveCheckpoint(run);
            this.World = null;
        }

        /// <summary>
        /// Record one original participant's vote. The pact needs every original participant (including eliminated
        /// and currently offline ones) to agree; while anyone has not voted, the run stays paused in this phase.
        /// Any "no" ends the run.
        /// </summary>
        public TxResult VotePact(string participantId, bool accept)
        {
            if (this.Phase != RunPhase.PactVote)
                return TxResult.Reject("no pact vote open");
            var run = this.Run;
            var result = this.Ledger.Commit(new RunTransaction { TxId = $"pactvote:{run.RunId}:{participantId}", Kind = TxKind.PactVote, ParticipantId = participantId, Flag = accept });
            if (!result.Succeeded)
                return result;
            if (!accept)
            {
                run.Pact.VoteOpen = false;
                this.Ledger.Commit(new RunTransaction { TxId = $"end:{run.RunId}", Kind = TxKind.EndRun, Flag = false });
                this.Phase = RunPhase.Finished;
            }
            else if (run.OriginalParticipants.All(id => run.Pact.Votes.TryGetValue(id, out bool v) && v))
            {
                this.Ledger.Commit(new RunTransaction { TxId = $"pact:{run.RunId}", Kind = TxKind.PactConsume });
                this.Phase = RunPhase.BetweenArenas; // the next BeginArena retries the same boss with the pact mechanic
            }
            this.store?.SaveCheckpoint(run);
            return result;
        }

        public IEnumerable<string> PendingPactVoters() =>
            this.Run.OriginalParticipants.Where(id => !this.Run.Pact.Votes.ContainsKey(id));
    }
}
