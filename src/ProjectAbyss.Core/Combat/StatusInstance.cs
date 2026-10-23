using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Core.Combat
{
    public sealed class StatusInstance
    {
        public StatusType Type { get; set; }
        public int ExpireTick { get; set; }
        public int NextTick { get; set; }
        public float Potency { get; set; }
        public int Stacks { get; set; } = 1;
        public int SourceId { get; set; }
    }

    public sealed class BuffInstance
    {
        public string BuffId { get; set; } = "";
        public int ExpireTick { get; set; }
        public float DamageMultiplier { get; set; } = 1f;
        public float DamageTakenMultiplier { get; set; } = 1f;
        public float MoveSpeedMultiplier { get; set; } = 1f;
        public float ResourceGainMultiplier { get; set; } = 1f;
        public float Shield { get; set; }
    }
}
