using System;
using System.Linq;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Randomness;

namespace ProjectAbyss.Core.Arena
{
    /// <summary>Builds a randomised arena from a hand-made template and validates it; falls back to a safe arena if needed.</summary>
    public sealed class ArenaGenerator
    {
        public const int MaxAttempts = 12;
        private readonly GameData data;

        public ArenaGenerator(GameData data)
        {
            this.data = data;
        }

        public ArenaMap Generate(string templateId, ulong seed)
        {
            if (!this.data.Arenas.TryGetValue(templateId, out var template))
                throw new ArgumentException($"Unknown arena template '{templateId}'.", nameof(templateId));
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                ulong attemptSeed = DeterministicRng.Mix(seed, (ulong)attempt);
                var map = Build(template, attemptSeed);
                if (ArenaValidator.Validate(map).IsValid)
                    return map;
            }
            return this.BuildFallback(template.IsBossArena, seed);
        }

        public ArenaMap BuildFallback(bool bossArena, ulong seed)
        {
            var fallback = this.data.Arenas.Values.FirstOrDefault(a => a.IsFallback && a.IsBossArena == bossArena)
                ?? this.data.Arenas.Values.First(a => a.IsFallback);
            var map = Build(fallback, seed, randomSlots: false);
            var result = ArenaValidator.Validate(map);
            if (!result.IsValid)
                throw new InvalidOperationException($"Fallback arena '{fallback.Id}' is invalid: {string.Join("; ", result.Errors)}");
            map.UsedFallback = true;
            return map;
        }

        public static ArenaMap Build(ArenaTemplateDef template, ulong seed, bool randomSlots = true)
        {
            var rng = new DeterministicRng(seed, 0xA4E9A);
            int h = template.Rows.Count;
            int w = template.Rows[0].Length;
            var map = ArenaMap.Create(w, h);
            map.TemplateId = template.Id;
            map.Seed = seed;
            map.IsBossArena = template.IsBossArena;
            for (int y = 0; y < h; y++)
            {
                string row = template.Rows[y];
                for (int x = 0; x < w; x++)
                {
                    char c = row[x];
                    TileType tile = TileType.Floor;
                    switch (c)
                    {
                        case '#':
                            tile = TileType.Wall;
                            break;
                        case '~':
                            tile = TileType.Hazard;
                            break;
                        case 'o':
                            tile = randomSlots && rng.Chance(template.ObstacleChance) ? TileType.Obstacle : TileType.Floor;
                            break;
                        case 'h':
                            tile = randomSlots && rng.Chance(template.HazardChance) ? TileType.Hazard : TileType.Floor;
                            break;
                        case 'P':
                            map.PlayerStarts.Add(new TilePoint(x, y));
                            break;
                        case 'S':
                            map.SpawnPoints.Add(new TilePoint(x, y));
                            break;
                        case 'E':
                            map.Exits.Add(new TilePoint(x, y));
                            break;
                        case 'B':
                            map.BossSpawn = new TilePoint(x, y);
                            break;
                        case '.':
                            break;
                        default:
                            throw new GameDataException($"Arena '{template.Id}': unknown tile '{c}' at {x},{y}.");
                    }
                    map.Set(x, y, tile);
                }
            }
            // Shuffle spawn points so enemy groups appear from different sides per seed.
            for (int i = map.SpawnPoints.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (map.SpawnPoints[i], map.SpawnPoints[j]) = (map.SpawnPoints[j], map.SpawnPoints[i]);
            }
            return map;
        }
    }
}
