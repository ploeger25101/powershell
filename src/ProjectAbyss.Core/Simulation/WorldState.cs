using System.Collections.Generic;
using System.Text.Json;
using ProjectAbyss.Core.Arena;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Simulation
{
    public enum EncounterKind
    {
        Waves,
        Survival,
        EliteHunt,
        Defense,
        Boss,
        /// <summary>Never ends on its own; used by the developer mode sandbox and tests.</summary>
        Training,
    }

    public enum EncounterStatus
    {
        InProgress,
        Cleared,
        Failed,
    }

    public sealed class EncounterState
    {
        public EncounterKind Kind { get; set; }
        public EncounterStatus Status { get; set; } = EncounterStatus.InProgress;
        public List<List<string>> Waves { get; set; } = new();
        public List<string> EliteEnemies { get; set; } = new();
        public int WaveIndex { get; set; } = -1;
        public int NextWaveTick { get; set; }
        public int SurvivalEndTick { get; set; }
        public string? BossId { get; set; }
        public int BossEntityId { get; set; } = -1;
        public bool IsRiskArena { get; set; }
        /// <summary>Player count locked at arena start; scaling never changes mid-arena (see design rule §19).</summary>
        public int ScalingPlayerCount { get; set; } = 1;
        public int EndTick { get; set; } = -1;
    }

    /// <summary>
    /// Complete, serialisable state of one arena. Everything needed for crash recovery and reconnect snapshots
    /// lives here; <see cref="CombatWorld"/> holds only derived caches.
    /// </summary>
    public sealed class WorldState
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string RunId { get; set; } = "";
        public int Level { get; set; } = 1;
        public string DifficultyId { get; set; } = "normal";
        public int Tick { get; set; }
        public int NextEntityId { get; set; } = 1;
        public RngState Rng { get; set; }
        public ArenaMap Arena { get; set; } = new();
        public List<PlayerEntity> Players { get; set; } = new();
        public List<EnemyEntity> Enemies { get; set; } = new();
        public List<ProjectileEntity> Projectiles { get; set; } = new();
        public EncounterState Encounter { get; set; } = new();
        public bool PactActive { get; set; }

        public WorldState DeepClone()
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(this, GameData.JsonOptions);
            return JsonSerializer.Deserialize<WorldState>(bytes, GameData.JsonOptions)!;
        }
    }
}
