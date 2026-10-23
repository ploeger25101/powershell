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
        public static int PhaseIndexFor(BossDef def, float hpFraction)
        {
            int index = 0;
            for (int i = 0; i < def.Phases.Count; i++)
            {
                if (hpFraction <= def.Phases[i].StartsAtHpFraction)
                    index = i;
            }
            return index;
        }

        /// <summary>Attacks a boss may use in its current phase, including co-op and pact additions.</summary>
        public List<string> AvailableBossAttacks(EnemyEntity boss)
        {
            var def = this.Data.Bosses[boss.DefId];
            var phase = def.Phases[boss.Boss!.PhaseIndex];
            var list = new List<string>(phase.Attacks);
            if (boss.Boss.ActivePlayerCount >= phase.CoopMinPlayers)
                list.AddRange(phase.CoopAttacks);
            if (boss.Boss.PactActive && !string.IsNullOrEmpty(def.PactAttack))
                list.Add(def.PactAttack);
            return list;
        }

        private void UpdateBoss(EnemyEntity boss)
        {
            int tick = this.State.Tick;
            var def = this.Data.Bosses[boss.DefId];
            var b = boss.Boss!;
            boss.IsMoving = false;

            int phase = PhaseIndexFor(def, boss.Hp / boss.MaxHp);
            if (phase > b.PhaseIndex)
            {
                b.PhaseIndex = phase;
                boss.Action = null;
                b.NextAttackTick = tick + SimTime.Ticks(1.5f);
                var ev = this.EmitAt(SimEventType.BossPhaseChanged, boss.Position, boss.Id, boss.Id, key: def.Phases[phase].Music);
                ev.IntValue = phase;
            }
            if (tick < b.StaggeredUntilTick)
            {
                boss.State = AiState.Interrupted;
                return;
            }
            if (boss.Action != null)
            {
                this.UpdateEnemyAction(boss);
                return;
            }

            var target = this.TargetablePlayers.OrderBy(p => Vec2.DistanceSquared(p.Position, boss.Position)).ThenBy(p => p.Id).FirstOrDefault();
            if (target == null)
                return;
            boss.TargetId = target.Id;
            float dist = Vec2.Distance(boss.Position, target.Position);

            if (tick >= b.NextAttackTick)
            {
                var available = this.AvailableBossAttacks(boss)
                    .Select(id => def.Attacks.First(a => a.Id == id))
                    .Where(a => (!boss.AttackReadyTick.TryGetValue(a.Id, out int r) || tick >= r) && dist <= a.UseRange + target.Radius)
                    .ToList();
                if (available.Count > 0)
                {
                    var atk = available[this.rng.NextInt(0, available.Count)];
                    this.BeginEnemyAction(boss, AttackRegistry.BossKey(def.Id, atk.Id), atk, target.Position);
                    var phaseDef = def.Phases[b.PhaseIndex];
                    b.NextAttackTick = boss.Action!.EndTick + SimTime.Ticks(phaseDef.AttackIntervalSeconds * this.BossIntervalMultiplier());
                    return;
                }
            }

            if (dist > 40f)
            {
                var dir = this.ChaseDirection(boss, target, this.State.Arena.HasLineOfSight(boss.Position, target.Position));
                float speed = boss.MoveSpeed * def.Phases[b.PhaseIndex].MoveSpeedMultiplier * StatusRules.SlowMultiplier(boss.Statuses, tick);
                var before = boss.Position;
                boss.Position = this.State.Arena.SteerCircle(boss.Position, dir, speed * SimTime.TickSeconds, boss.Radius);
                boss.IsMoving = Vec2.DistanceSquared(before, boss.Position) > 0.0001f;
                boss.State = AiState.Chase;
            }
            boss.Aim = (target.Position - boss.Position).Angle;
            boss.Facing = Direction8Util.FromAngle(boss.Aim);
        }

        /// <summary>Higher difficulties shorten the pause between boss attacks.</summary>
        private float BossIntervalMultiplier() => 1f / (1f + 0.1f * this.Difficulty.ExtraBossAbilities);
    }
}
