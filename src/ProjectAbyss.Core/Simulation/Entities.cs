using System.Collections.Generic;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Simulation
{
    public enum LifeState
    {
        Alive,
        /// <summary>HP reached zero; can be revived by an ally inside the revive window.</summary>
        Downed,
        /// <summary>Lost a life and waits to respawn.</summary>
        Respawning,
        /// <summary>All lives used: no longer takes part in combat; state is kept for later revival.</summary>
        Eliminated,
    }

    public enum ActionKind
    {
        Basic,
        Special,
        Ability,
        Ultimate,
        Enemy,
    }

    /// <summary>
    /// An attack in progress. Timing is defined in simulation ticks; animations only visualise these phases.
    /// The attack definition is referenced by key so the state stays serialisable.
    /// </summary>
    public sealed class ActiveAction
    {
        public string AttackKey { get; set; } = "";
        public ActionKind Kind { get; set; }
        public int StartTick { get; set; }
        public int WindupTicks { get; set; }
        public int ActiveTicks { get; set; }
        public int RecoveryTicks { get; set; }
        public float Aim { get; set; }
        public Vec2 GroundTarget { get; set; }
        public bool ActivationHandled { get; set; }
        public List<int> HitTargets { get; set; } = new();
        public float DamageMultiplier { get; set; } = 1f;

        public int EndTick => this.StartTick + this.WindupTicks + this.ActiveTicks + this.RecoveryTicks;

        public ActionPhase PhaseAt(int tick)
        {
            int t = tick - this.StartTick;
            if (t < this.WindupTicks)
                return ActionPhase.Windup;
            if (t < this.WindupTicks + this.ActiveTicks)
                return ActionPhase.Active;
            if (t < this.WindupTicks + this.ActiveTicks + this.RecoveryTicks)
                return ActionPhase.Recovery;
            return ActionPhase.Done;
        }
    }

    public enum ActionPhase
    {
        Windup,
        Active,
        Recovery,
        Done,
    }

    public sealed class PlayerEntity
    {
        public int Id { get; set; }
        /// <summary>Stable run-participant id (verified identity), never a client-chosen display name.</summary>
        public string ParticipantId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ClassId { get; set; } = "";
        public string WeaponTypeId { get; set; } = "";
        public Vec2 Position { get; set; }
        public float Aim { get; set; }
        public Direction8 Facing { get; set; } = Direction8.Down;
        public bool IsMoving { get; set; }
        public float Hp { get; set; }
        public float MaxHp { get; set; }
        public float Armor { get; set; }
        public float MoveSpeed { get; set; }
        public float Radius { get; set; }
        public float CritChance { get; set; }
        /// <summary>Outgoing damage multiplier from equipment and level (buffs are applied separately).</summary>
        public float DamageMultiplier { get; set; } = 1f;
        public float Resource { get; set; }
        public float ResourceMax { get; set; }
        public int LastCombatTick { get; set; }
        public int Lives { get; set; }
        public LifeState LifeState { get; set; }
        public int LifeStateUntilTick { get; set; }
        public int ReviveProgressTicks { get; set; }
        public int DashCharges { get; set; }
        public int DashRechargeTicks { get; set; }
        public int DashEndTick { get; set; } = -1;
        public int DashInvulnerableUntilTick { get; set; } = -1;
        public Vec2 DashDirection { get; set; }
        public int HitInvulnerableUntilTick { get; set; } = -1;
        /// <summary>Reconnect protection: no damage taken, no actions, no interactions until this tick.</summary>
        public int ProtectedUntilTick { get; set; } = -1;
        public int PotionCharges { get; set; }
        public Dictionary<string, int> CooldownReadyTick { get; set; } = new();
        public ActiveAction? Action { get; set; }
        public List<string> EquippedAbilities { get; set; } = new();
        public string Ultimate { get; set; } = "";
        public List<StatusInstance> Statuses { get; set; } = new();
        public List<BuffInstance> Buffs { get; set; } = new();
        public bool Connected { get; set; } = true;
        public uint LastProcessedInput { get; set; }
        public InputButtons LastHeld { get; set; }
        /// <summary>Hit-reaction animation marker (visual only).</summary>
        public int LastHitTick { get; set; } = -1000;

        public bool IsDashing(int tick) => tick < this.DashEndTick;
        public bool IsProtected(int tick) => tick < this.ProtectedUntilTick;
        public bool CanBeDamaged(int tick) =>
            this.LifeState == LifeState.Alive && tick >= this.DashInvulnerableUntilTick && tick >= this.HitInvulnerableUntilTick && !this.IsProtected(tick);
    }

    public enum AiState
    {
        SeekTarget,
        ChoosePosition,
        Chase,
        KeepDistance,
        PrepareAttack,
        Attack,
        Recover,
        Flee,
        Support,
        Summon,
        Interrupted,
        Dead,
    }

    public sealed class EnemyEntity
    {
        public int Id { get; set; }
        public string DefId { get; set; } = "";
        public bool IsBoss { get; set; }
        public bool IsElite { get; set; }
        public Vec2 Position { get; set; }
        public float Aim { get; set; }
        public Direction8 Facing { get; set; } = Direction8.Down;
        public bool IsMoving { get; set; }
        public float Hp { get; set; }
        public float MaxHp { get; set; }
        public float Armor { get; set; }
        public float MoveSpeed { get; set; }
        public float Radius { get; set; }
        public float DamageMultiplier { get; set; } = 1f;
        public AiState State { get; set; } = AiState.SeekTarget;
        public int TargetId { get; set; } = -1;
        public int NextDecisionTick { get; set; }
        public ActiveAction? Action { get; set; }
        public Dictionary<string, int> AttackReadyTick { get; set; } = new();
        public List<StatusInstance> Statuses { get; set; } = new();
        public int StunnedUntilTick { get; set; } = -1;
        public int LastStunTick { get; set; } = -10000;
        public int StunsInWindow { get; set; }
        public int LastHitTick { get; set; } = -1000;
        public Vec2 Knockback { get; set; }
        public BossState? Boss { get; set; }
    }

    public sealed class BossState
    {
        public int PhaseIndex { get; set; }
        public float Stagger { get; set; }
        public int StaggeredUntilTick { get; set; } = -1;
        public int StaggerImmuneUntilTick { get; set; } = -1;
        public int NextAttackTick { get; set; }
        public bool PactActive { get; set; }
        public int ActivePlayerCount { get; set; } = 1;
        public int AttackCursor { get; set; }
    }

    public sealed class ProjectileEntity
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public bool FromPlayer { get; set; }
        public string AttackKey { get; set; } = "";
        public Vec2 Position { get; set; }
        public Vec2 Velocity { get; set; }
        public float Radius { get; set; }
        public int ExpireTick { get; set; }
        public int PierceLeft { get; set; }
        public float DamageMultiplier { get; set; } = 1f;
        public List<int> HitTargets { get; set; } = new();
    }
}
