using System;
using System.Linq;
using ProjectAbyss.Core.Arena;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Simulation
{
    public sealed partial class CombatWorld
    {
        public const float WaveDelaySeconds = 1.5f;
        public const float EliteHpMultiplier = 2.5f;
        public const float EliteDamageMultiplier = 1.4f;

        private int ScalingIndex => Math.Clamp(this.State.Encounter.ScalingPlayerCount, 1, 4) - 1;

        public EnemyEntity SpawnEnemy(string defId, Vec2 position, bool elite)
        {
            var def = this.Data.Enemies[defId];
            var scaling = this.Balance.Scaling;
            elite |= def.IsElite;
            float hp = def.MaxHp * scaling.EnemyHp[this.ScalingIndex] * this.Difficulty.EnemyHp * (elite ? EliteHpMultiplier : 1f);
            var e = new EnemyEntity
            {
                Id = this.NewEntityId(),
                DefId = defId,
                IsElite = elite,
                Position = position,
                Hp = hp,
                MaxHp = hp,
                Armor = def.Armor,
                MoveSpeed = def.MoveSpeed,
                Radius = def.Radius * (elite ? 1.2f : 1f),
                DamageMultiplier = scaling.EnemyDamage[this.ScalingIndex] * this.Difficulty.EnemyDamage * (elite ? EliteDamageMultiplier : 1f),
                NextDecisionTick = this.State.Tick + SimTime.Ticks(0.5f) + this.rng.NextInt(0, 20),
            };
            this.State.Enemies.Add(e);
            this.EmitAt(SimEventType.EnemySpawned, position, e.Id, key: defId, flag: elite);
            return e;
        }

        public EnemyEntity SpawnBoss(string bossId, bool pactActive)
        {
            var def = this.Data.Bosses[bossId];
            var spawn = this.State.Arena.BossSpawn ?? this.State.Arena.SpawnPoints.FirstOrDefault();
            float hp = def.MaxHp * this.Balance.Scaling.BossHp[this.ScalingIndex] * this.Difficulty.EnemyHp;
            var boss = new EnemyEntity
            {
                Id = this.NewEntityId(),
                DefId = bossId,
                IsBoss = true,
                Position = spawn.Center,
                Hp = hp,
                MaxHp = hp,
                Armor = def.Armor,
                MoveSpeed = def.MoveSpeed,
                Radius = def.Radius,
                DamageMultiplier = this.Balance.Scaling.EnemyDamage[this.ScalingIndex] * this.Difficulty.EnemyDamage,
                Boss = new BossState
                {
                    PactActive = pactActive,
                    ActivePlayerCount = this.State.Encounter.ScalingPlayerCount,
                    NextAttackTick = this.State.Tick + SimTime.Ticks(2f),
                },
            };
            this.State.Enemies.Add(boss);
            this.State.Encounter.BossEntityId = boss.Id;
            var ev = this.EmitAt(SimEventType.BossPhaseChanged, boss.Position, boss.Id, boss.Id, key: def.Phases[0].Music);
            ev.IntValue = 0;
            return boss;
        }

        private void UpdateEncounter()
        {
            var enc = this.State.Encounter;
            int tick = this.State.Tick;
            if (enc.Status != EncounterStatus.InProgress)
                return;

            if (this.State.Players.Count > 0 && this.State.Players.All(p => p.LifeState == LifeState.Eliminated))
            {
                this.EndEncounter(EncounterStatus.Failed);
                this.Emit(SimEventType.PartyDefeated);
                return;
            }

            switch (enc.Kind)
            {
                case EncounterKind.Training:
                    break;

                case EncounterKind.Boss:
                    if (enc.BossEntityId < 0)
                        this.SpawnBoss(enc.BossId!, this.State.PactActive);
                    else if (this.FindEnemy(enc.BossEntityId) is not { Hp: > 0 })
                    {
                        foreach (var e in this.State.Enemies)
                            e.Hp = 0; // adds vanish with the boss
                        this.EndEncounter(EncounterStatus.Cleared);
                    }
                    break;

                case EncounterKind.Survival:
                    if (enc.SurvivalEndTick == 0)
                        enc.SurvivalEndTick = tick + SimTime.Ticks(60f);
                    if (tick >= enc.SurvivalEndTick)
                    {
                        foreach (var e in this.State.Enemies)
                            e.Hp = 0;
                        this.EndEncounter(EncounterStatus.Cleared);
                    }
                    else if (!this.State.Enemies.Any(e => e.Hp > 0) && tick >= enc.NextWaveTick && enc.Waves.Count > 0)
                    {
                        enc.WaveIndex = (enc.WaveIndex + 1) % enc.Waves.Count; // waves loop until the timer runs out
                        this.SpawnWave(enc.Waves[enc.WaveIndex]);
                        enc.NextWaveTick = tick + SimTime.Ticks(WaveDelaySeconds);
                    }
                    break;

                default:
                    if (this.State.Enemies.Any(e => e.Hp > 0))
                        break;
                    if (enc.WaveIndex + 1 >= enc.Waves.Count)
                    {
                        this.EndEncounter(EncounterStatus.Cleared);
                        break;
                    }
                    if (enc.NextWaveTick == 0)
                        enc.NextWaveTick = tick + (enc.WaveIndex < 0 ? SimTime.Ticks(1f) : SimTime.Ticks(WaveDelaySeconds));
                    if (tick >= enc.NextWaveTick)
                    {
                        enc.WaveIndex++;
                        enc.NextWaveTick = 0;
                        this.SpawnWave(enc.Waves[enc.WaveIndex]);
                    }
                    break;
            }
        }

        private void EndEncounter(EncounterStatus status)
        {
            var enc = this.State.Encounter;
            enc.Status = status;
            enc.EndTick = this.State.Tick;
            this.State.Projectiles.Clear();
            if (status == EncounterStatus.Cleared)
                this.Emit(SimEventType.ArenaCleared);
        }

        private void SpawnWave(System.Collections.Generic.List<string> wave)
        {
            var points = this.State.Arena.SpawnPoints;
            var enc = this.State.Encounter;
            for (int i = 0; i < wave.Count; i++)
            {
                var basePoint = points[i % points.Count].Center;
                var jitter = new Vec2(this.rng.NextFloat(-6f, 6f), this.rng.NextFloat(-6f, 6f));
                var pos = this.State.Arena.CircleOverlapsSolid(basePoint + jitter, 7f) ? basePoint : basePoint + jitter;
                bool elite = enc.EliteEnemies.Contains(wave[i]) && i == 0;
                this.SpawnEnemy(wave[i], pos, elite);
            }
            var ev = this.EmitAt(SimEventType.WaveStarted, Vec2.Zero, amount: wave.Count);
            ev.IntValue = enc.WaveIndex;
        }
    }
}
