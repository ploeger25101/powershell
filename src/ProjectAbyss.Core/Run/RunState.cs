using System;
using System.Collections.Generic;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Items;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Run
{
    public enum RunStatus
    {
        Active,
        Won,
        Lost,
    }

    public sealed class RunParticipant
    {
        /// <summary>Stable, verified identity (see Net.ParticipantIdentity).</summary>
        public string ParticipantId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ClassId { get; set; } = "warrior";
        public string WeaponTypeId { get; set; } = "";
        public int Level { get; set; } = 1;
        public int Xp { get; set; }
        public int UnspentSkillPoints { get; set; }
        public Dictionary<string, int> SkillAllocation { get; set; } = new();
        public int SkillResetCount { get; set; }
        public List<string> EquippedAbilities { get; set; } = new();
        public string Ultimate { get; set; } = "";
        public int Lives { get; set; } = 3;
        public bool Eliminated { get; set; }
        public float? Hp { get; set; }
        public float Resource { get; set; }
        public int PotionCharges { get; set; } = 2;
        public Dictionary<string, float> RemainingCooldowns { get; set; } = new();
        public long Gold { get; set; }
        public Dictionary<string, int> Materials { get; set; } = new();
        public Dictionary<string, ItemInstance> Equipment { get; set; } = new();
        public Inventory Inventory { get; set; } = new();
        public Inventory RunChest { get; set; } = new();
        /// <summary>Rewards that did not fit; kept until the player makes room (never destroyed silently).</summary>
        public List<ItemInstance> PendingRewards { get; set; } = new();
    }

    public sealed class PactState
    {
        public bool Used { get; set; }
        public bool VoteOpen { get; set; }
        public int BossLevel { get; set; }
        public Dictionary<string, bool> Votes { get; set; } = new();
    }

    /// <summary>Everything belonging to the current run (design §28). Persisted at safe transitions and checkpoints.</summary>
    public sealed class RunState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string RunId { get; set; } = "";
        public ulong Seed { get; set; }
        public string DifficultyId { get; set; } = "normal";
        public DateTime CreatedUtc { get; set; }
        public bool IsDeveloperRun { get; set; }
        public string ContentHash { get; set; } = "";
        public RunStatus Status { get; set; } = RunStatus.Active;
        public int CurrentLevel { get; set; } = 1;
        public int ArenaAttempt { get; set; }
        /// <summary>Biome number → chosen boss id; fixed when the run starts.</summary>
        public Dictionary<int, string> BossSelection { get; set; } = new();
        public List<string> OriginalParticipants { get; set; } = new();
        public List<RunParticipant> Participants { get; set; } = new();
        public PactState Pact { get; set; } = new();
        public long NextItemSerial { get; set; } = 1;
        public RngState LootRng { get; set; }
        /// <summary>Highest journal sequence number reflected in this state.</summary>
        public long JournalSequence { get; set; }
        /// <summary>Ids of all transactions applied in this run; makes every transaction idempotent.</summary>
        public HashSet<string> AppliedTransactions { get; set; } = new();

        public RunParticipant? Find(string participantId) => this.Participants.Find(p => p.ParticipantId == participantId);

        public RunParticipant Get(string participantId) =>
            this.Find(participantId) ?? throw new KeyNotFoundException($"Participant '{participantId}' is not part of run {this.RunId}.");

        public string NewItemId() => $"itm-{this.RunId}-{this.NextItemSerial++}";
    }
}
