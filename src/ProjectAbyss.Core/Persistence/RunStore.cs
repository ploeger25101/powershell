using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Persistence
{
    /// <summary>Crash snapshot taken during combat: run state plus the full arena state at the same moment.</summary>
    public sealed class RecoverySnapshot
    {
        public RunState Run { get; set; } = new();
        public WorldState? World { get; set; }
    }

    public sealed class RecoveredRun
    {
        public RunState Run { get; init; } = new();
        /// <summary>Non-null when the host crashed during an arena; resume combat from here.</summary>
        public WorldState? World { get; init; }
        public int ReplayedTransactions { get; init; }
        public List<string> Notes { get; init; } = new();
    }

    /// <summary>
    /// Files of one run (in its own directory, never inside Stardew Valley save folders):
    /// <c>run.json</c> checkpoint at safe transitions, <c>recovery.json</c> mid-combat snapshot, <c>journal.log</c> transactions.
    /// </summary>
    public sealed class RunStore
    {
        public const string RunKind = "run";
        public const string RecoveryKind = "recovery";

        private readonly string directory;
        private long fileSequence;

        public RunStore(string directory)
        {
            this.directory = directory;
            this.Journal = new TransactionJournal(Path.Combine(directory, "journal.log"));
        }

        public TransactionJournal Journal { get; }
        public string RunPath => Path.Combine(this.directory, "run.json");
        public string RecoveryPath => Path.Combine(this.directory, "recovery.json");
        public bool Exists => File.Exists(this.RunPath) || File.Exists(this.RunPath + ".bak");

        /// <summary>Safe transition (arena cleared, shop, level change): write the checkpoint, drop the combat snapshot, compact the journal.</summary>
        public void SaveCheckpoint(RunState run)
        {
            run.JournalSequence = this.Journal.LastSequence;
            EnvelopeIO.Save(this.RunPath, RunKind, RunState.CurrentSchemaVersion, ++this.fileSequence, run);
            AtomicFile.Delete(this.RecoveryPath);
            this.Journal.TruncateThrough(run.JournalSequence);
        }

        /// <summary>Mid-combat snapshot (periodic and after each critical event).</summary>
        public void SaveRecovery(RunState run, WorldState world)
        {
            run.JournalSequence = this.Journal.LastSequence;
            EnvelopeIO.Save(this.RecoveryPath, RecoveryKind, RunState.CurrentSchemaVersion, ++this.fileSequence, new RecoverySnapshot { Run = run, World = world });
        }

        public RecoveredRun? Load(TransactionProcessor processor)
        {
            var notes = new List<string>();
            RunState? run = null;
            WorldState? world = null;

            var recovery = EnvelopeIO.Load<RecoverySnapshot>(this.RecoveryPath, RecoveryKind, RunState.CurrentSchemaVersion);
            var checkpoint = EnvelopeIO.Load<RunState>(this.RunPath, RunKind, RunState.CurrentSchemaVersion);
            if (recovery.Value != null && (checkpoint.Value == null || recovery.Value.Run.JournalSequence >= checkpoint.Value.JournalSequence))
            {
                run = recovery.Value.Run;
                world = recovery.Value.World;
                notes.Add("resuming from combat snapshot" + (recovery.Status == LoadStatus.RecoveredFromBackup ? " (backup copy)" : ""));
            }
            else if (checkpoint.Value != null)
            {
                run = checkpoint.Value;
                if (checkpoint.Status == LoadStatus.RecoveredFromBackup)
                    notes.Add("checkpoint restored from backup copy: " + checkpoint.Error);
                if (recovery.Status == LoadStatus.Corrupt)
                    notes.Add("combat snapshot unusable: " + recovery.Error);
            }
            if (run == null)
                return null;

            var entries = this.Journal.ReadAll(out bool damaged);
            if (damaged || this.Journal.RepairedOnOpen)
                notes.Add("discarded an incomplete journal entry (unconfirmed transaction)");
            int replayed = 0;
            var replayedTx = new List<RunTransaction>();
            foreach (var e in entries.Where(e => e.Seq > run.JournalSequence))
            {
                var result = processor.Apply(run, e.Tx);
                if (result.Succeeded)
                {
                    replayed++;
                    replayedTx.Add(e.Tx);
                }
                run.JournalSequence = e.Seq;
            }
            if (world != null)
                Reconcile(run, world, replayedTx);
            return new RecoveredRun { Run = run, World = world, ReplayedTransactions = replayed, Notes = notes };
        }

        /// <summary>
        /// Bring an older combat snapshot in line with transactions confirmed after it: rewarded kills stay dead and
        /// lost lives stay lost, so nothing confirmed can be gained or suffered twice.
        /// </summary>
        public static void Reconcile(RunState run, WorldState world, List<RunTransaction> replayed)
        {
            string prefix = $"kill:{run.RunId}:{run.ArenaAttempt}:";
            foreach (var tx in replayed.Where(t => t.Kind == TxKind.KillReward && t.TxId.StartsWith(prefix, System.StringComparison.Ordinal)))
            {
                var parts = tx.TxId.Substring(prefix.Length).Split(':');
                if (parts.Length > 0 && int.TryParse(parts[0], out int entityId))
                {
                    var enemy = world.Enemies.Find(e => e.Id == entityId);
                    if (enemy != null)
                    {
                        enemy.Hp = 0;
                        enemy.State = AiState.Dead;
                    }
                }
            }
            foreach (var p in world.Players)
            {
                var rp = run.Find(p.ParticipantId);
                if (rp == null || rp.Lives >= p.Lives)
                    continue;
                p.Lives = rp.Lives;
                p.Action = null;
                p.Hp = 0;
                p.LifeState = rp.Eliminated ? LifeState.Eliminated : LifeState.Respawning;
                p.LifeStateUntilTick = world.Tick + SimTime.Ticks(CombatWorld.RespawnDelaySeconds);
            }
        }

        public void DeleteAll()
        {
            AtomicFile.Delete(this.RunPath);
            AtomicFile.Delete(this.RecoveryPath);
            this.Journal.Delete();
        }
    }
}
