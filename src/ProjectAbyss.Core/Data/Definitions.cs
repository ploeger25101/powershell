using System;
using System.Collections.Generic;

namespace ProjectAbyss.Core.Data
{
    // Definitions are plain data loaded from JSON (content/data/*.json). Times are authored in seconds and
    // converted to simulation ticks by the systems that use them (see SimTime).

    public enum DamageType
    {
        Physical,
        Fire,
        Frost,
        Lightning,
        Poison,
        Arcane,
        Holy,
        Shadow,
    }

    public enum StatusType
    {
        Bleed,
        Poison,
        Burn,
        Frost,
        Shock,
        Stun,
    }

    public enum HitShape
    {
        /// <summary>Cone in front of the attacker along the exact aim angle.</summary>
        Sector,
        /// <summary>Circle centred on the attacker (or on <see cref="AttackDef.Offset"/> along the aim).</summary>
        Circle,
        /// <summary>Thick line from the attacker along the aim.</summary>
        Capsule,
        /// <summary>Spawns projectile(s) travelling along the exact aim angle.</summary>
        Projectile,
        /// <summary>No hit; used for pure self-buffs.</summary>
        None,
    }

    public sealed class StatusApplicationDef
    {
        public StatusType Type { get; set; }
        public float Chance { get; set; } = 1f;
        public float DurationSeconds { get; set; }
        /// <summary>Damage per second for DoTs, slow fraction for frost, damage-taken bonus for shock.</summary>
        public float Potency { get; set; }
    }

    public sealed class ProjectileDef
    {
        public float Speed { get; set; } = 240f;
        public float Radius { get; set; } = 4f;
        public float LifetimeSeconds { get; set; } = 1.5f;
        public int Pierce { get; set; }
        public int Count { get; set; } = 1;
        public float SpreadDegrees { get; set; }
    }

    /// <summary>Shared definition for player attacks, abilities and enemy/boss attacks.</summary>
    public sealed class AttackDef
    {
        public string Id { get; set; } = "";
        public HitShape Shape { get; set; } = HitShape.Sector;
        public float Range { get; set; } = 32f;
        public float HalfAngleDegrees { get; set; } = 45f;
        public float Width { get; set; } = 8f;
        /// <summary>Distance along the aim at which a Circle shape is centred (0 = on the attacker).</summary>
        public float Offset { get; set; }
        public float WindupSeconds { get; set; } = 0.1f;
        public float ActiveSeconds { get; set; } = 0.1f;
        public float RecoverySeconds { get; set; } = 0.2f;
        public float Damage { get; set; } = 10f;
        public DamageType DamageType { get; set; } = DamageType.Physical;
        public float Knockback { get; set; }
        public float StaggerDamage { get; set; }
        public float LungeDistance { get; set; }
        public int MaxTargets { get; set; } = 99;
        public float ResourceGainOnHit { get; set; }
        public float MoveSpeedMultiplier { get; set; } = 0.35f;
        public bool CanCrit { get; set; } = true;
        /// <summary>For enemy attacks: telegraph shown to players during windup.</summary>
        public bool Telegraphed { get; set; }
        /// <summary>For enemy attacks: maximum distance to target at which the AI starts this attack.</summary>
        public float UseRange { get; set; } = 32f;
        /// <summary>Enemy/boss attacks: cooldown before the same attack can be chosen again.</summary>
        public float CooldownSeconds { get; set; }
        /// <summary>Enemy/boss attacks: centre the hit area on the target position captured at windup start.</summary>
        public bool TargetGround { get; set; }
        public List<StatusApplicationDef> Statuses { get; set; } = new();
        public ProjectileDef? Projectile { get; set; }
        /// <summary>Optional summon (enemy definition id) spawned when the attack becomes active.</summary>
        public string? SummonEnemyId { get; set; }
        public int SummonCount { get; set; }
        /// <summary>Heals allies in range instead of damaging enemies (support enemies).</summary>
        public float AllyHeal { get; set; }
        /// <summary>Player attack: incoming hits during the active window are negated and answered with a riposte.</summary>
        public bool Parry { get; set; }
        public float RiposteDamage { get; set; }
        public float ParryResourceGain { get; set; }
    }

    public sealed class BuffDef
    {
        public string Id { get; set; } = "";
        public float DurationSeconds { get; set; }
        public float DamageMultiplier { get; set; } = 1f;
        public float DamageTakenMultiplier { get; set; } = 1f;
        public float MoveSpeedMultiplier { get; set; } = 1f;
        public float ResourceGainMultiplier { get; set; } = 1f;
        public float Shield { get; set; }
        /// <summary>Also applied to allies within this radius (0 = self only).</summary>
        public float AllyRadius { get; set; }
    }

    public sealed class AbilityDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string ClassId { get; set; } = "";
        public bool IsUltimate { get; set; }
        public float CooldownSeconds { get; set; }
        public float ResourceCost { get; set; }
        public AttackDef Attack { get; set; } = new();
        public BuffDef? SelfBuff { get; set; }
        /// <summary>Animation key used by the renderer (eight-direction set unless <see cref="Omnidirectional"/>).</summary>
        public string Animation { get; set; } = "ability";
        public bool Omnidirectional { get; set; }
    }

    public sealed class WeaponTypeDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ClassId { get; set; } = "";
        public bool TwoHanded { get; set; }
        public AttackDef Basic { get; set; } = new();
        public AttackDef Special { get; set; } = new();
        public float SpecialCooldownSeconds { get; set; } = 4f;
        public string PassiveDescription { get; set; } = "";
        public string Sound { get; set; } = "";
    }

    public sealed class ClassResourceDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public float Max { get; set; } = 100f;
        public float Start { get; set; }
        public float RegenPerSecond { get; set; }
        public float GainOnHitDealt { get; set; }
        public float GainOnDamageTaken { get; set; }
        public float GainOnDash { get; set; }
        /// <summary>Loss per second while no enemy has been hit or damage taken for <see cref="DecayDelaySeconds"/>.</summary>
        public float DecayPerSecond { get; set; }
        public float DecayDelaySeconds { get; set; } = 4f;
    }

    public sealed class ClassDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool UnlockedByDefault { get; set; }
        public float MaxHp { get; set; } = 100f;
        public float Armor { get; set; }
        public float MoveSpeed { get; set; } = 90f;
        public float Radius { get; set; } = 7f;
        public float CritChance { get; set; } = 0.05f;
        public float HpPerLevel { get; set; } = 6f;
        public float ArmorPerLevel { get; set; } = 1f;
        public ClassResourceDef Resource { get; set; } = new();
        public List<string> WeaponTypes { get; set; } = new();
        public List<string> SkillBranches { get; set; } = new();
        public List<string> StarterAbilities { get; set; } = new();
        public string BaseUltimate { get; set; } = "";
        public Dictionary<DamageType, float> Resistances { get; set; } = new();
    }

    public enum EnemyRole
    {
        Melee,
        Ranged,
        Tank,
        Assassin,
        Caster,
        Support,
        Summoner,
    }

    public sealed class EnemyDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Biome { get; set; } = 1;
        public EnemyRole Role { get; set; }
        public float MaxHp { get; set; } = 30f;
        public float Armor { get; set; }
        public float MoveSpeed { get; set; } = 60f;
        public float Radius { get; set; } = 7f;
        public float AggroRange { get; set; } = 400f;
        /// <summary>Distance the AI tries to keep from its target (0 = close in).</summary>
        public float PreferredRange { get; set; }
        /// <summary>Reaction delay before an AI re-evaluates its target; prevents frame-perfect reactions.</summary>
        public float ReactionSeconds { get; set; } = 0.35f;
        public float SpawnCost { get; set; } = 1f;
        public int Xp { get; set; } = 5;
        public int GoldMin { get; set; } = 1;
        public int GoldMax { get; set; } = 4;
        public float FleeHpFraction { get; set; }
        public List<AttackDef> Attacks { get; set; } = new();
        public Dictionary<DamageType, float> Resistances { get; set; } = new();
        public Dictionary<StatusType, float> StatusResistances { get; set; } = new();
        public bool IsElite { get; set; }
        public string Sprite { get; set; } = "";
    }

    public sealed class BossPhaseDef
    {
        /// <summary>Phase is active while HP fraction is at or below this value (first phase uses 1.0).</summary>
        public float StartsAtHpFraction { get; set; } = 1f;
        public float AttackIntervalSeconds { get; set; } = 1.5f;
        public float MoveSpeedMultiplier { get; set; } = 1f;
        public List<string> Attacks { get; set; } = new();
        /// <summary>Extra attacks only enabled with this many or more players (co-op mechanics).</summary>
        public int CoopMinPlayers { get; set; } = 2;
        public List<string> CoopAttacks { get; set; } = new();
        public string Music { get; set; } = "";
    }

    public sealed class BossDef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Biome { get; set; } = 1;
        public float MaxHp { get; set; } = 1500f;
        public float Armor { get; set; } = 20f;
        public float MoveSpeed { get; set; } = 45f;
        public float Radius { get; set; } = 20f;
        public float StaggerMax { get; set; } = 300f;
        public float StaggerDurationSeconds { get; set; } = 3f;
        /// <summary>After a stagger ends, stagger damage is ignored for this long (prevents stun-lock).</summary>
        public float StaggerImmunitySeconds { get; set; } = 8f;
        /// <summary>Multiplier applied to the duration of each further stun within the diminishing window.</summary>
        public float StunDiminishFactor { get; set; } = 0.5f;
        public string ArenaTemplate { get; set; } = "";
        public List<BossPhaseDef> Phases { get; set; } = new();
        public List<AttackDef> Attacks { get; set; } = new();
        /// <summary>Attack id added to every phase after the Pact of the Last Chance has been invoked.</summary>
        public string PactAttack { get; set; } = "";
        public Dictionary<DamageType, float> Resistances { get; set; } = new();
        public int Xp { get; set; } = 200;
        public int GoldMin { get; set; } = 150;
        public int GoldMax { get; set; } = 250;
        public string Sprite { get; set; } = "";
    }

    public sealed class ArenaTemplateDef
    {
        public string Id { get; set; } = "";
        public int Biome { get; set; } = 1;
        public bool IsBossArena { get; set; }
        public bool IsFallback { get; set; }
        /// <summary>
        /// Row strings. Legend: '#' wall, '.' floor, 'P' player start, 'S' enemy spawn, 'E' exit,
        /// 'o' optional obstacle slot, 'h' optional hazard slot, '~' fixed hazard (spikes), 'B' boss spawn.
        /// </summary>
        public List<string> Rows { get; set; } = new();
        public float ObstacleChance { get; set; } = 0.5f;
        public float HazardChance { get; set; } = 0.35f;
    }
}
