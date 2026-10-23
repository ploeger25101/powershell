using System.Collections.Generic;

namespace ProjectAbyss.Core.Data
{
    /// <summary>Central, data-driven balancing values (content/data/balance.json).</summary>
    public sealed class BalanceConfig
    {
        public int SchemaVersion { get; set; } = 1;
        public DashConfig Dash { get; set; } = new();
        public LivesConfig Lives { get; set; } = new();
        public ReconnectConfig Reconnect { get; set; } = new();
        public CombatConfig Combat { get; set; } = new();
        public ScalingConfig Scaling { get; set; } = new();
        public PotionConfig Potion { get; set; } = new();
        public ProgressionConfig Progression { get; set; } = new();
        public EconomyConfig Economy { get; set; } = new();
    }

    public sealed class DashConfig
    {
        public int Charges { get; set; } = 2;
        public float RechargeSeconds { get; set; } = 3f;
        public float DurationSeconds { get; set; } = 0.22f;
        public float InvulnerableSeconds { get; set; } = 0.14f;
        public float Distance { get; set; } = 64f;
    }

    public sealed class LivesConfig
    {
        public int StartLives { get; set; } = 3;
        public float ReviveWindowSeconds { get; set; } = 12f;
        public float ReviveChannelSeconds { get; set; } = 2.5f;
        public float ReviveRadius { get; set; } = 28f;
        public float ReviveHpFraction { get; set; } = 0.3f;
        public float RespawnHpFraction { get; set; } = 0.5f;
        public float RespawnInvulnerableSeconds { get; set; } = 2f;
        public int PactLives { get; set; } = 1;
    }

    public sealed class ReconnectConfig
    {
        public float ProtectionSeconds { get; set; } = 2f;
        /// <summary>Host declares a client disconnected after this long without any packet.</summary>
        public float TimeoutSeconds { get; set; } = 6f;
    }

    public sealed class CombatConfig
    {
        /// <summary>Armor mitigation = armor / (armor + ArmorConstant).</summary>
        public float ArmorConstant { get; set; } = 100f;
        public float MaxArmorMitigation { get; set; } = 0.75f;
        public float CritMultiplier { get; set; } = 1.5f;
        public float MaxResistance { get; set; } = 0.75f;
        public float MinResistance { get; set; } = -1f;
        public float StatusTickSeconds { get; set; } = 0.5f;
        public float PlayerHitInvulnerableSeconds { get; set; } = 0.25f;
        /// <summary>Repeated stuns on the same normal enemy within this window are shortened.</summary>
        public float StunDiminishWindowSeconds { get; set; } = 6f;
        public int MaxActiveProjectiles { get; set; } = 400;
    }

    public sealed class ScalingConfig
    {
        /// <summary>Index = player count − 1.</summary>
        public List<float> BossHp { get; set; } = new() { 1f, 1.75f, 2.5f, 3.25f };
        public List<float> EnemyBudget { get; set; } = new() { 1f, 1.5f, 2f, 2.5f };
        public List<float> EnemyHp { get; set; } = new() { 1f, 1.1f, 1.2f, 1.3f };
        public List<float> EnemyDamage { get; set; } = new() { 1f, 1.05f, 1.1f, 1.15f };
        public List<DifficultyConfig> Difficulties { get; set; } = new();
        public float BaseThreatBudget { get; set; } = 6f;
        public float ThreatBudgetPerLevel { get; set; } = 1.25f;
    }

    public sealed class DifficultyConfig
    {
        public string Id { get; set; } = "normal";
        public string Name { get; set; } = "Normal";
        public float EnemyHp { get; set; } = 1f;
        public float EnemyDamage { get; set; } = 1f;
        public float Budget { get; set; } = 1f;
        public float EliteChance { get; set; } = 0.05f;
        public int ExtraBossAbilities { get; set; }
        public string? UnlockedBy { get; set; }
    }

    public sealed class PotionConfig
    {
        public int StartCharges { get; set; } = 2;
        public int MaxCharges { get; set; } = 3;
        public float HealFraction { get; set; } = 0.35f;
        public float CooldownSeconds { get; set; } = 1f;
    }

    public sealed class ProgressionConfig
    {
        public int MaxLevel { get; set; } = 20;
        /// <summary>XP required to go from level (index+1) to (index+2).</summary>
        public List<int> XpToNextLevel { get; set; } = new();
        public int SkillPointsPerLevel { get; set; } = 1;
        public int SkillPointsPerBossMilestone { get; set; } = 1;
    }

    public sealed class EconomyConfig
    {
        public float SellPriceFraction { get; set; } = 0.25f;
        public List<int> UpgradeGoldCost { get; set; } = new();
        public int SkillResetBaseCost { get; set; } = 150;
        public float SkillResetCostGrowth { get; set; } = 1.6f;
        public int InventorySlots { get; set; } = 24;
        public int RunChestSlots { get; set; } = 16;
    }
}
