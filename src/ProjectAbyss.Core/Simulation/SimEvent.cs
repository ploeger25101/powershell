using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Simulation
{
    public enum SimEventType : byte
    {
        ActionStarted,
        Telegraph,
        Hit,
        PlayerHit,
        Parried,
        StatusApplied,
        EnemyKilled,
        Dash,
        PotionUsed,
        PlayerDowned,
        PlayerRevived,
        LifeLost,
        PlayerRespawned,
        PlayerEliminated,
        BossPhaseChanged,
        BossStaggered,
        WaveStarted,
        ArenaCleared,
        PartyDefeated,
        EnemySpawned,
        Interrupted,
    }

    /// <summary>Things that happened during a tick: drives presentation (sound, VFX, numbers) and run bookkeeping.</summary>
    public sealed class SimEvent
    {
        public SimEventType Type { get; set; }
        public int Tick { get; set; }
        public int SourceId { get; set; }
        public int TargetId { get; set; }
        public float Amount { get; set; }
        public bool Flag { get; set; }
        public string Key { get; set; } = "";
        public Vec2 Position { get; set; }
        public int IntValue { get; set; }
        public int IntValue2 { get; set; }

        public override string ToString() => $"[{this.Tick}] {this.Type} src={this.SourceId} tgt={this.TargetId} amt={this.Amount:0.#} key={this.Key}";
    }
}
