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
        public const float StaggeredDamageTakenMultiplier = 1.25f;

        private IReadOnlyDictionary<DamageType, float> ResistancesOf(EnemyEntity e) =>
            e.IsBoss ? this.Data.Bosses[e.DefId].Resistances : this.Data.Enemies[e.DefId].Resistances;

        private void DamageEnemy(PlayerEntity attacker, EnemyEntity enemy, AttackDef atk, string attackKey, float multiplier)
        {
            int tick = this.State.Tick;
            float taken = StatusRules.ShockTakenMultiplier(enemy.Statuses, tick);
            if (enemy.Boss != null && tick < enemy.Boss.StaggeredUntilTick)
                taken *= StaggeredDamageTakenMultiplier;
            var result = DamageCalculator.Compute(atk.Damage, atk.DamageType, multiplier, atk.CanCrit, attacker.CritChance, this.rng,
                enemy.Armor, this.ResistancesOf(enemy), taken, 0f, this.Balance.Combat);
            enemy.Hp -= result.Amount;
            enemy.LastHitTick = tick;
            attacker.LastCombatTick = tick;
            this.EmitAt(SimEventType.Hit, enemy.Position, attacker.Id, enemy.Id, result.Amount, attackKey, result.Critical);
            this.AddResource(attacker, this.ClassOf(attacker).Resource.GainOnHitDealt + atk.ResourceGainOnHit);

            if (atk.Knockback > 0 && !enemy.IsBoss)
                enemy.Knockback += (enemy.Position - attacker.Position).Normalized() * atk.Knockback;
            if (enemy.Boss != null && atk.StaggerDamage > 0)
                this.AddStagger(enemy, atk.StaggerDamage);
            foreach (var status in atk.Statuses)
                this.TryApplyStatusToEnemy(attacker.Id, enemy, status);

            if (enemy.Hp <= 0)
                this.KillEnemy(enemy, attacker.Id);
        }

        private void TryApplyStatusToEnemy(int sourceId, EnemyEntity enemy, StatusApplicationDef status)
        {
            float resist = 0f;
            if (!enemy.IsBoss && this.Data.Enemies[enemy.DefId].StatusResistances.TryGetValue(status.Type, out float r))
                resist = Math.Clamp(r, 0f, 1f);
            if (!this.rng.Chance(status.Chance * (1f - resist)))
                return;
            int tick = this.State.Tick;
            int duration = SimTime.Ticks(status.DurationSeconds * (1f - resist * 0.5f));
            if (status.Type == StatusType.Stun)
            {
                if (enemy.Boss != null)
                {
                    // Bosses cannot be stunned directly: stuns feed the stagger bar instead (no stun-lock).
                    this.AddStagger(enemy, status.DurationSeconds * 60f);
                    return;
                }
                duration = this.DiminishedStun(enemy, duration);
                if (duration <= 0)
                    return;
                enemy.StunnedUntilTick = Math.Max(enemy.StunnedUntilTick, tick + duration);
                if (enemy.Action != null && enemy.Action.PhaseAt(tick) == ActionPhase.Windup)
                {
                    enemy.Action = null;
                    this.EmitAt(SimEventType.Interrupted, enemy.Position, sourceId, enemy.Id);
                }
                enemy.State = AiState.Interrupted;
            }
            int interval = SimTime.Ticks(this.Balance.Combat.StatusTickSeconds);
            StatusRules.Apply(enemy.Statuses, status.Type, tick, duration, status.Potency, sourceId, interval);
            this.EmitAt(SimEventType.StatusApplied, enemy.Position, sourceId, enemy.Id, key: status.Type.ToString());
        }

        /// <summary>Repeated stuns inside the diminishing window are halved each time; the third is ignored.</summary>
        private int DiminishedStun(EnemyEntity enemy, int duration)
        {
            int tick = this.State.Tick;
            int window = SimTime.Ticks(this.Balance.Combat.StunDiminishWindowSeconds);
            if (tick - enemy.LastStunTick > window)
                enemy.StunsInWindow = 0;
            enemy.LastStunTick = tick;
            enemy.StunsInWindow++;
            return enemy.StunsInWindow switch
            {
                1 => duration,
                2 => duration / 2,
                _ => 0,
            };
        }

        private void AddStagger(EnemyEntity boss, float amount)
        {
            var b = boss.Boss!;
            int tick = this.State.Tick;
            if (tick < b.StaggeredUntilTick || tick < b.StaggerImmuneUntilTick)
                return;
            var def = this.Data.Bosses[boss.DefId];
            b.Stagger += amount;
            if (b.Stagger < def.StaggerMax)
                return;
            b.Stagger = 0;
            b.StaggeredUntilTick = tick + SimTime.Ticks(def.StaggerDurationSeconds);
            b.StaggerImmuneUntilTick = b.StaggeredUntilTick + SimTime.Ticks(def.StaggerImmunitySeconds);
            boss.Action = null;
            this.EmitAt(SimEventType.BossStaggered, boss.Position, 0, boss.Id);
        }

        private void KillEnemy(EnemyEntity enemy, int killerId)
        {
            enemy.Hp = 0;
            enemy.State = AiState.Dead;
            enemy.Action = null;
            int xp;
            int gold;
            if (enemy.IsBoss)
            {
                var def = this.Data.Bosses[enemy.DefId];
                xp = def.Xp;
                gold = this.rng.NextInt(def.GoldMin, def.GoldMax + 1);
            }
            else
            {
                var def = this.Data.Enemies[enemy.DefId];
                xp = def.Xp * (enemy.IsElite ? 3 : 1);
                gold = this.rng.NextInt(def.GoldMin, def.GoldMax + 1) * (enemy.IsElite ? 3 : 1);
            }
            // Amount carries the elite flag because the entity is removed at the end of the tick.
            var e = this.EmitAt(SimEventType.EnemyKilled, enemy.Position, killerId, enemy.Id, enemy.IsElite ? 1f : 0f, enemy.DefId, enemy.IsBoss);
            e.IntValue = xp;
            e.IntValue2 = gold;
        }

        /// <summary>Damage from an enemy attack or projectile to a player. Respects i-frames, parries, protection and shields.</summary>
        private void DamagePlayer(EnemyEntity? source, int sourceId, PlayerEntity p, AttackDef atk, float multiplier)
        {
            int tick = this.State.Tick;
            if (!p.CanBeDamaged(tick))
                return;

            if (p.Action != null && p.Action.PhaseAt(tick) == ActionPhase.Active && this.Attacks.Get(p.Action.AttackKey).Parry)
            {
                var parry = this.Attacks.Get(p.Action.AttackKey);
                this.EmitAt(SimEventType.Parried, p.Position, p.Id, sourceId);
                this.AddResource(p, parry.ParryResourceGain);
                p.LastCombatTick = tick;
                if (source != null && source.Hp > 0 && parry.RiposteDamage > 0 && Vec2.Distance(source.Position, p.Position) <= parry.Range + source.Radius + 24f)
                {
                    var riposte = new AttackDef { Damage = parry.RiposteDamage, DamageType = parry.DamageType, StaggerDamage = parry.StaggerDamage, CanCrit = true };
                    this.DamageEnemy(p, source, riposte, p.Action.AttackKey, p.Action.DamageMultiplier);
                }
                return;
            }

            float taken = StatusRules.ShockTakenMultiplier(p.Statuses, tick);
            float shield = 0f;
            foreach (var b in p.Buffs)
            {
                taken *= b.DamageTakenMultiplier;
                shield += b.Shield;
            }
            var result = DamageCalculator.Compute(atk.Damage, atk.DamageType, multiplier, false, 0f, this.rng,
                p.Armor, this.ClassOf(p).Resistances, taken, shield, this.Balance.Combat);
            this.ConsumeShields(p, result.AbsorbedByShield);
            p.Hp -= result.Amount;
            p.LastHitTick = tick;
            p.LastCombatTick = tick;
            p.HitInvulnerableUntilTick = tick + SimTime.Ticks(this.Balance.Combat.PlayerHitInvulnerableSeconds);
            this.EmitAt(SimEventType.PlayerHit, p.Position, sourceId, p.Id, result.Amount, atk.Id);
            this.AddResource(p, this.ClassOf(p).Resource.GainOnDamageTaken);

            foreach (var status in atk.Statuses)
            {
                if (!this.rng.Chance(status.Chance))
                    continue;
                StatusRules.Apply(p.Statuses, status.Type, tick, SimTime.Ticks(status.DurationSeconds), status.Potency, sourceId, SimTime.Ticks(this.Balance.Combat.StatusTickSeconds));
                if (status.Type == StatusType.Stun)
                    p.Action = null;
            }
            if (p.Hp <= 0)
                this.DownPlayer(p);
        }

        private void ConsumeShields(PlayerEntity p, float absorbed)
        {
            foreach (var b in p.Buffs.Where(b => b.Shield > 0))
            {
                if (absorbed <= 0)
                    break;
                float use = Math.Min(b.Shield, absorbed);
                b.Shield -= use;
                absorbed -= use;
            }
        }

        private void UpdateStatuses()
        {
            int tick = this.State.Tick;
            int interval = SimTime.Ticks(this.Balance.Combat.StatusTickSeconds);
            float tickSeconds = this.Balance.Combat.StatusTickSeconds;
            foreach (var enemy in this.State.Enemies)
            {
                if (enemy.Hp <= 0)
                    continue;
                foreach (var s in enemy.Statuses)
                {
                    if (!StatusRules.IsDamageOverTime(s.Type) || tick < s.NextTick || s.ExpireTick <= tick)
                        continue;
                    s.NextTick = tick + interval;
                    float dmg = s.Potency * s.Stacks * tickSeconds;
                    enemy.Hp -= dmg;
                    this.EmitAt(SimEventType.Hit, enemy.Position, s.SourceId, enemy.Id, dmg, "dot:" + s.Type);
                    if (enemy.Hp <= 0)
                    {
                        this.KillEnemy(enemy, s.SourceId);
                        break;
                    }
                }
                StatusRules.RemoveExpired(enemy.Statuses, tick);
            }
            foreach (var p in this.State.Players)
            {
                p.Buffs.RemoveAll(b => b.ExpireTick <= tick);
                if (p.LifeState != LifeState.Alive)
                {
                    p.Statuses.Clear();
                    continue;
                }
                foreach (var s in p.Statuses)
                {
                    if (!StatusRules.IsDamageOverTime(s.Type) || tick < s.NextTick || s.ExpireTick <= tick || p.IsProtected(tick))
                        continue;
                    s.NextTick = tick + interval;
                    float dmg = s.Potency * s.Stacks * tickSeconds;
                    p.Hp -= dmg;
                    this.EmitAt(SimEventType.PlayerHit, p.Position, s.SourceId, p.Id, dmg, "dot:" + s.Type);
                    if (p.Hp <= 0)
                    {
                        this.DownPlayer(p);
                        break;
                    }
                }
                StatusRules.RemoveExpired(p.Statuses, tick);
                this.ApplyHazards(p);
            }
        }

        public const float HazardDamagePerSecond = 16f;

        private void ApplyHazards(PlayerEntity p)
        {
            int tick = this.State.Tick;
            if (p.LifeState != LifeState.Alive || p.IsDashing(tick) || !p.CanBeDamaged(tick) || tick % 30 != 0)
                return;
            var t = Arena.TilePoint.FromWorld(p.Position);
            if (this.State.Arena.Get(t.X, t.Y) != Arena.TileType.Hazard)
                return;
            var hazard = new AttackDef { Id = "hazard", Damage = HazardDamagePerSecond * 0.5f, DamageType = DamageType.Physical };
            this.DamagePlayer(null, 0, p, hazard, this.Difficulty.EnemyDamage);
        }
    }
}
