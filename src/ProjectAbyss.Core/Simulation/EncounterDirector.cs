using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Simulation
{
    /// <summary>
    /// Builds enemy waves from a threat budget (level, difficulty, player count, arena modifiers, enemy costs).
    /// Caps simultaneous enemies to keep AI cost and screen readability bounded.
    /// </summary>
    public sealed class EncounterDirector
    {
        public const int MaxEnemiesPerWave = 12;
        public const int MaxElitesPerEncounter = 2;
        private readonly GameData data;

        public EncounterDirector(GameData data)
        {
            this.data = data;
        }

        public float ThreatBudget(int level, string difficultyId, int playerCount, bool risk)
        {
            var s = this.data.Balance.Scaling;
            var diff = s.Difficulties.FirstOrDefault(d => d.Id == difficultyId) ?? s.Difficulties[0];
            float budget = (s.BaseThreatBudget + s.ThreatBudgetPerLevel * (level - 1)) * diff.Budget * s.EnemyBudget[Math.Clamp(playerCount, 1, 4) - 1];
            return risk ? budget * 1.35f : budget;
        }

        public EncounterState Build(EncounterKind kind, int biome, int level, string difficultyId, int playerCount, bool risk, DeterministicRng rng)
        {
            var pool = this.PoolFor(biome);
            // biomes without content yet fall back to the closest lower biome that has enemies
            for (int b = biome - 1; pool.Count == 0 && b >= 1; b--)
                pool = this.PoolFor(b);
            if (pool.Count == 0)
                throw new InvalidOperationException($"No enemies defined for biome {biome} or any lower biome.");
            float budget = this.ThreatBudget(level, difficultyId, playerCount, risk);
            var diff = this.data.Balance.Scaling.Difficulties.FirstOrDefault(d => d.Id == difficultyId) ?? this.data.Balance.Scaling.Difficulties[0];
            var enc = new EncounterState { Kind = kind, IsRiskArena = risk, ScalingPlayerCount = playerCount };

            int waveCount = kind switch
            {
                EncounterKind.EliteHunt => 1,
                EncounterKind.Survival => 3,
                _ => budget > 14 ? 3 : 2,
            };
            float perWave = budget / waveCount;
            int elites = 0;
            for (int w = 0; w < waveCount; w++)
            {
                var wave = new List<string>();
                float left = perWave;
                if (kind == EncounterKind.EliteHunt && w == 0 || (rng.Chance(diff.EliteChance + (risk ? 0.15f : 0f)) && elites < MaxElitesPerEncounter))
                {
                    var elite = pool[rng.NextInt(0, pool.Count)];
                    wave.Add(elite.Id);
                    enc.EliteEnemies.Add(elite.Id);
                    left -= elite.SpawnCost * 3f;
                    elites++;
                }
                int guard = 0;
                while (left > 0 && wave.Count < MaxEnemiesPerWave && guard++ < 100)
                {
                    var affordable = pool.Where(e => e.SpawnCost <= left + 0.001f).ToList();
                    if (affordable.Count == 0)
                        break;
                    var pick = affordable[rng.NextInt(0, affordable.Count)];
                    // at most two supports/summoners per wave so waves stay fair
                    if ((pick.Role is EnemyRole.Support or EnemyRole.Summoner) && wave.Count(id => this.data.Enemies[id].Role == pick.Role) >= 2)
                    {
                        left -= 0.25f;
                        continue;
                    }
                    wave.Add(pick.Id);
                    left -= pick.SpawnCost;
                }
                if (wave.Count == 0)
                    wave.Add(pool.OrderBy(e => e.SpawnCost).First().Id);
                enc.Waves.Add(wave);
            }
            return enc;
        }

        private List<EnemyDef> PoolFor(int biome) =>
            this.data.Enemies.Values.Where(e => e.Biome == biome && !e.IsElite)
                .Where(e => !this.IsSummonOnly(e.Id))
                .OrderBy(e => e.Id, StringComparer.Ordinal)
                .ToList();

        private bool IsSummonOnly(string enemyId) =>
            this.data.Enemies[enemyId].SpawnCost < 0.5f
            && (this.data.Enemies.Values.Any(e => e.Attacks.Any(a => a.SummonEnemyId == enemyId))
                || this.data.Bosses.Values.Any(b => b.Attacks.Any(a => a.SummonEnemyId == enemyId)));

        public static EncounterState BossEncounter(string bossId, int playerCount) =>
            new() { Kind = EncounterKind.Boss, BossId = bossId, ScalingPlayerCount = playerCount };
    }
}
