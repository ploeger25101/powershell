using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Simulation
{
    public sealed partial class CombatWorld
    {
        private void UpdateEnemies()
        {
            this.EnsureFlowField();
            // iterate over a copy: summons may add enemies during the loop
            var enemies = this.State.Enemies.ToArray();
            foreach (var e in enemies)
            {
                if (e.Hp <= 0)
                    continue;
                if (e.Knockback.LengthSquared > 0.01f)
                {
                    e.Position = this.State.Arena.MoveCircle(e.Position, e.Knockback * 0.25f, e.Radius);
                    e.Knockback *= 0.75f;
                }
                if (e.Boss != null)
                    this.UpdateBoss(e);
                else
                    this.UpdateEnemyAi(e);
            }
            this.SeparateEnemies();
        }

        private EnemyDef DefOf(EnemyEntity e) => this.Data.Enemies[e.DefId];

        private IEnumerable<PlayerEntity> TargetablePlayers =>
            this.State.Players.Where(p => p.LifeState == LifeState.Alive);

        private void UpdateEnemyAi(EnemyEntity e)
        {
            int tick = this.State.Tick;
            var def = this.DefOf(e);
            e.IsMoving = false;

            if (tick < e.StunnedUntilTick)
            {
                e.State = AiState.Interrupted;
                e.Action = null;
                return;
            }
            if (e.Action != null)
            {
                this.UpdateEnemyAction(e);
                return;
            }

            var target = this.FindPlayer(e.TargetId);
            if (target == null || target.LifeState != LifeState.Alive)
            {
                e.State = AiState.SeekTarget;
                target = null;
            }

            if (tick >= e.NextDecisionTick)
            {
                e.NextDecisionTick = tick + SimTime.Ticks(def.ReactionSeconds) + this.rng.NextInt(0, 8);
                target = this.ChooseTarget(e, def) ?? target;
                e.TargetId = target?.Id ?? -1;
                if (target != null && this.TryStartEnemyAttack(e, def, target))
                    return;
            }
            if (target == null)
            {
                e.State = AiState.SeekTarget;
                return;
            }

            float dist = Vec2.Distance(e.Position, target.Position);
            bool los = this.State.Arena.HasLineOfSight(e.Position, target.Position);
            float speed = e.MoveSpeed * StatusRules.SlowMultiplier(e.Statuses, tick);
            Vec2 dir;

            if (!los && def.PreferredRange > 0)
            {
                // ranged roles without line of sight reposition towards the target first
                e.State = AiState.Chase;
                dir = this.ChaseDirection(e, target, los);
            }
            else if (def.FleeHpFraction > 0 && e.Hp / e.MaxHp < def.FleeHpFraction)
            {
                e.State = AiState.Flee;
                dir = (e.Position - target.Position).Normalized();
            }
            else if (def.PreferredRange > 0)
            {
                if (dist < def.PreferredRange * 0.75f)
                {
                    e.State = AiState.KeepDistance;
                    dir = (e.Position - target.Position).Normalized();
                }
                else if (dist > def.PreferredRange * 1.15f)
                {
                    e.State = AiState.Chase;
                    dir = this.ChaseDirection(e, target, los);
                }
                else
                {
                    // strafe around the target instead of standing still
                    e.State = AiState.ChoosePosition;
                    var away = (e.Position - target.Position).Normalized();
                    float side = (e.Id & 1) == 0 ? 1f : -1f;
                    dir = new Vec2(-away.Y * side, away.X * side) * 0.6f;
                }
            }
            else
            {
                float stopAt = def.Attacks.Min(a => a.UseRange) * 0.8f + target.Radius;
                if (dist > stopAt)
                {
                    e.State = AiState.Chase;
                    dir = this.ChaseDirection(e, target, los);
                }
                else
                {
                    e.State = AiState.PrepareAttack;
                    dir = Vec2.Zero;
                }
            }
            if (def.Role == EnemyRole.Support)
                e.State = AiState.Support;

            if (dir.LengthSquared > 0.0001f)
            {
                var before = e.Position;
                e.Position = this.State.Arena.SteerCircle(e.Position, dir.Normalized(), dir.Length * speed * SimTime.TickSeconds, e.Radius);
                e.IsMoving = Vec2.DistanceSquared(before, e.Position) > 0.0001f;
                e.Facing = Direction8Util.FromVector(dir, e.Facing);
            }
            else
            {
                e.Facing = Direction8Util.FromVector(target.Position - e.Position, e.Facing);
            }
            e.Aim = (target.Position - e.Position).Angle;
        }

        private Vec2 ChaseDirection(EnemyEntity e, PlayerEntity target, bool los)
        {
            if (los && Vec2.Distance(e.Position, target.Position) < 96f && this.State.Arena.HasClearPath(e.Position, target.Position, e.Radius))
                return (target.Position - e.Position).Normalized();
            var d = this.flow.DirectionFrom(e.Position);
            return d.LengthSquared > 0 ? d : (target.Position - e.Position).Normalized();
        }

        /// <summary>Role-specific target selection; never omniscient re-targeting every frame (see ReactionSeconds).</summary>
        private PlayerEntity? ChooseTarget(EnemyEntity e, EnemyDef def)
        {
            var candidates = this.TargetablePlayers.Where(p => Vec2.Distance(p.Position, e.Position) <= def.AggroRange).ToList();
            if (candidates.Count == 0)
                return null;
            switch (def.Role)
            {
                case EnemyRole.Assassin:
                    return candidates.OrderBy(p => p.Hp / p.MaxHp).ThenBy(p => p.Id).First();
                case EnemyRole.Tank:
                    // tanks stick to their current target if still valid
                    var current = candidates.Find(p => p.Id == e.TargetId);
                    return current ?? candidates.OrderBy(p => Vec2.DistanceSquared(p.Position, e.Position)).First();
                default:
                    return candidates.OrderBy(p => Vec2.DistanceSquared(p.Position, e.Position)).ThenBy(p => p.Id).First();
            }
        }

        private bool TryStartEnemyAttack(EnemyEntity e, EnemyDef def, PlayerEntity target)
        {
            int tick = this.State.Tick;
            float dist = Vec2.Distance(e.Position, target.Position);
            bool los = this.State.Arena.HasLineOfSight(e.Position, target.Position);
            foreach (var atk in def.Attacks)
            {
                if (e.AttackReadyTick.TryGetValue(atk.Id, out int ready) && tick < ready)
                    continue;
                if (atk.AllyHeal > 0)
                {
                    var hurt = this.State.Enemies.Where(o => o.Hp > 0 && o.Id != e.Id && o.Hp < o.MaxHp * 0.7f && Vec2.Distance(o.Position, e.Position) <= atk.UseRange)
                        .OrderBy(o => o.Hp / o.MaxHp).FirstOrDefault();
                    if (hurt == null)
                        continue;
                    this.BeginEnemyAction(e, AttackRegistry.EnemyKey(e.DefId, atk.Id), atk, hurt.Position);
                    return true;
                }
                if (atk.SummonEnemyId != null)
                {
                    int alive = this.State.Enemies.Count(o => o.Hp > 0 && o.DefId == atk.SummonEnemyId);
                    if (alive >= 4)
                        continue;
                }
                else if (dist > atk.UseRange + target.Radius || !los)
                {
                    continue;
                }
                this.BeginEnemyAction(e, AttackRegistry.EnemyKey(e.DefId, atk.Id), atk, target.Position);
                return true;
            }
            return false;
        }

        private void BeginEnemyAction(EnemyEntity e, string key, AttackDef atk, Vec2 targetPos)
        {
            int tick = this.State.Tick;
            e.AttackReadyTick[atk.Id] = tick + SimTime.Ticks(atk.CooldownSeconds) + SimTime.Ticks(atk.WindupSeconds + atk.ActiveSeconds + atk.RecoverySeconds);
            // Aim is locked when the windup starts so attacks can be read and dodged.
            e.Aim = (targetPos - e.Position).Angle;
            e.Facing = Direction8Util.FromAngle(e.Aim);
            e.Action = new ActiveAction
            {
                AttackKey = key,
                Kind = ActionKind.Enemy,
                StartTick = tick,
                WindupTicks = SimTime.Ticks(atk.WindupSeconds),
                ActiveTicks = Math.Max(1, SimTime.Ticks(atk.ActiveSeconds)),
                RecoveryTicks = SimTime.Ticks(atk.RecoverySeconds),
                Aim = e.Aim,
                GroundTarget = targetPos,
                DamageMultiplier = e.DamageMultiplier,
            };
            e.State = AiState.Attack;
            var ev = this.EmitAt(atk.Telegraphed ? SimEventType.Telegraph : SimEventType.ActionStarted, e.Position, e.Id, key: key, amount: e.Aim);
            ev.IntValue = e.Action.WindupTicks;
        }

        private void UpdateEnemyAction(EnemyEntity e)
        {
            var action = e.Action!;
            int tick = this.State.Tick;
            var phase = action.PhaseAt(tick);
            if (phase == ActionPhase.Done)
            {
                e.Action = null;
                e.State = AiState.Recover;
                return;
            }
            if (phase != ActionPhase.Active)
                return;
            var atk = this.Attacks.Get(action.AttackKey);
            if (!action.ActivationHandled)
            {
                action.ActivationHandled = true;
                if (atk.Shape == HitShape.Projectile)
                    this.SpawnProjectiles(e.Id, false, action.AttackKey, atk, e.Position, action.Aim, action.DamageMultiplier);
                if (atk.SummonEnemyId != null)
                {
                    for (int i = 0; i < atk.SummonCount; i++)
                    {
                        var offset = Vec2.FromAngle(this.rng.NextFloat(0, MathF.PI * 2)) * (e.Radius + 14f);
                        this.SpawnEnemy(atk.SummonEnemyId, e.Position + offset, false);
                    }
                    e.State = AiState.Summon;
                }
                if (atk.AllyHeal > 0)
                {
                    foreach (var ally in this.State.Enemies.Where(o => o.Hp > 0 && Vec2.Distance(o.Position, action.GroundTarget) <= atk.Range))
                        ally.Hp = Math.Min(ally.MaxHp, ally.Hp + atk.AllyHeal);
                    return;
                }
            }
            if (atk.LungeDistance > 0)
                e.Position = this.State.Arena.MoveCircle(e.Position, Vec2.FromAngle(action.Aim) * (atk.LungeDistance / action.ActiveTicks), e.Radius);
            if (atk.Shape is HitShape.Projectile or HitShape.None || atk.Damage <= 0)
                return;
            foreach (var p in this.State.Players)
            {
                if (p.LifeState != LifeState.Alive || action.HitTargets.Contains(p.Id))
                    continue;
                if (!this.AttackHits(atk, e.Position, action.Aim, action.GroundTarget, p.Position, p.Radius))
                    continue;
                action.HitTargets.Add(p.Id);
                this.DamagePlayer(e, e.Id, p, atk, action.DamageMultiplier);
            }
        }

        private void SeparateEnemies()
        {
            var list = this.State.Enemies;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a.Hp <= 0)
                    continue;
                for (int j = i + 1; j < list.Count; j++)
                {
                    var b = list[j];
                    if (b.Hp <= 0)
                        continue;
                    float minDist = a.Radius + b.Radius;
                    var delta = b.Position - a.Position;
                    float dSq = delta.LengthSquared;
                    if (dSq >= minDist * minDist || dSq < 1e-6f)
                        continue;
                    float d = MathF.Sqrt(dSq);
                    var push = delta / d * ((minDist - d) * 0.5f);
                    if (!a.IsBoss)
                        a.Position = this.State.Arena.MoveCircle(a.Position, -push, a.Radius);
                    if (!b.IsBoss)
                        b.Position = this.State.Arena.MoveCircle(b.Position, push, b.Radius);
                }
            }
        }

        private void SpawnProjectiles(int ownerId, bool fromPlayer, string key, AttackDef atk, Vec2 origin, float aim, float multiplier)
        {
            var def = atk.Projectile!;
            int count = Math.Max(1, def.Count);
            float spread = def.SpreadDegrees * MathF.PI / 180f;
            for (int i = 0; i < count; i++)
            {
                if (this.State.Projectiles.Count >= this.Balance.Combat.MaxActiveProjectiles)
                    return;
                float angle = aim;
                if (count > 1)
                {
                    angle = def.SpreadDegrees >= 360f
                        ? aim + i * (2 * MathF.PI / count)
                        : aim - spread / 2 + spread * i / (count - 1);
                }
                this.State.Projectiles.Add(new ProjectileEntity
                {
                    Id = this.NewEntityId(),
                    OwnerId = ownerId,
                    FromPlayer = fromPlayer,
                    AttackKey = key,
                    Position = origin,
                    Velocity = Vec2.FromAngle(angle) * def.Speed,
                    Radius = def.Radius,
                    ExpireTick = this.State.Tick + SimTime.Ticks(def.LifetimeSeconds),
                    PierceLeft = def.Pierce,
                    DamageMultiplier = multiplier,
                });
            }
        }

        private void UpdateProjectiles()
        {
            int tick = this.State.Tick;
            var arena = this.State.Arena;
            foreach (var proj in this.State.Projectiles)
            {
                if (tick >= proj.ExpireTick)
                    continue;
                proj.Position += proj.Velocity * SimTime.TickSeconds;
                var t = Arena.TilePoint.FromWorld(proj.Position);
                if (arena.IsSolid(t.X, t.Y))
                {
                    proj.ExpireTick = tick;
                    continue;
                }
                var atk = this.Attacks.Get(proj.AttackKey);
                if (proj.FromPlayer)
                {
                    var owner = this.FindPlayer(proj.OwnerId);
                    if (owner == null)
                    {
                        proj.ExpireTick = tick;
                        continue;
                    }
                    foreach (var e in this.State.Enemies)
                    {
                        if (e.Hp <= 0 || proj.HitTargets.Contains(e.Id) || !Geometry.CircleIntersectsCircle(proj.Position, proj.Radius, e.Position, e.Radius))
                            continue;
                        proj.HitTargets.Add(e.Id);
                        this.DamageEnemy(owner, e, atk, proj.AttackKey, proj.DamageMultiplier);
                        if (proj.PierceLeft-- <= 0)
                        {
                            proj.ExpireTick = tick;
                            break;
                        }
                    }
                }
                else
                {
                    var owner = this.FindEnemy(proj.OwnerId);
                    foreach (var p in this.State.Players)
                    {
                        if (p.LifeState != LifeState.Alive || proj.HitTargets.Contains(p.Id) || !Geometry.CircleIntersectsCircle(proj.Position, proj.Radius, p.Position, p.Radius))
                            continue;
                        if (!p.CanBeDamaged(tick))
                            continue; // dashing through projectiles is allowed
                        proj.HitTargets.Add(p.Id);
                        this.DamagePlayer(owner, proj.OwnerId, p, atk, proj.DamageMultiplier);
                        if (proj.PierceLeft-- <= 0)
                        {
                            proj.ExpireTick = tick;
                            break;
                        }
                    }
                }
            }
            this.State.Projectiles.RemoveAll(p => p.ExpireTick <= tick);
        }
    }
}
