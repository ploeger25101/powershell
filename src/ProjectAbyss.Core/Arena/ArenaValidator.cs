using System;
using System.Collections.Generic;
using System.Linq;

namespace ProjectAbyss.Core.Arena
{
    public sealed class ArenaValidationResult
    {
        public List<string> Errors { get; } = new();
        public bool IsValid => this.Errors.Count == 0;
    }

    /// <summary>
    /// Rejects arenas that would be unfair or unplayable: unreachable exits/spawns, players starting in hazards,
    /// no room to dodge, hazards covering the safe area, or spawns right next to the players.
    /// </summary>
    public static class ArenaValidator
    {
        public const int MinSpawnDistanceTiles = 6;
        public const float MinSafeFraction = 0.6f;
        public const int DodgeWindowRadius = 3;
        public const int MinSafeTilesInDodgeWindow = 25;

        public static ArenaValidationResult Validate(ArenaMap map)
        {
            var result = new ArenaValidationResult();
            if (map.PlayerStarts.Count == 0)
                result.Errors.Add("no player start");
            if (map.Exits.Count == 0)
                result.Errors.Add("no exit");
            if (!map.IsBossArena && map.SpawnPoints.Count == 0)
                result.Errors.Add("no enemy spawn point");
            if (map.IsBossArena && map.BossSpawn == null)
                result.Errors.Add("boss arena without boss spawn");
            if (!result.IsValid)
                return result;

            var origin = map.PlayerStarts[0];
            bool[] walkReach = Flood(map, origin, safeOnly: false);
            bool[] safeReach = Flood(map, origin, safeOnly: true);
            bool Reach(bool[] r, TilePoint p) => map.InBounds(p.X, p.Y) && r[p.Y * map.Width + p.X];

            foreach (var p in map.PlayerStarts)
            {
                if (!map.IsSafe(p.X, p.Y))
                    result.Errors.Add($"player start {p} is not safe floor");
                else if (!Reach(safeReach, p))
                    result.Errors.Add($"player start {p} not reachable over safe ground");
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (map.Get(p.X + dx, p.Y + dy) == TileType.Hazard)
                            result.Errors.Add($"hazard adjacent to player start {p}");
                    }
                }
                if (CountSafeAround(map, p, DodgeWindowRadius) < MinSafeTilesInDodgeWindow)
                    result.Errors.Add($"not enough room to dodge around player start {p}");
            }
            foreach (var e in map.Exits.Where(e => !Reach(safeReach, e)))
                result.Errors.Add($"exit {e} not reachable over safe ground");
            foreach (var s in map.SpawnPoints)
            {
                if (map.IsSolid(s.X, s.Y) || !Reach(walkReach, s))
                    result.Errors.Add($"spawn {s} not reachable");
                if (map.PlayerStarts.Any(p => Math.Max(Math.Abs(p.X - s.X), Math.Abs(p.Y - s.Y)) < MinSpawnDistanceTiles))
                    result.Errors.Add($"spawn {s} too close to a player start");
            }
            if (map.BossSpawn is TilePoint b)
            {
                if (!Reach(walkReach, b))
                    result.Errors.Add("boss spawn not reachable");
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        if (map.IsSolid(b.X + dx, b.Y + dy))
                            result.Errors.Add("boss spawn needs a 5x5 open area");
                    }
                }
            }

            int walkable = 0;
            int safe = 0;
            for (int i = 0; i < walkReach.Length; i++)
            {
                if (walkReach[i])
                    walkable++;
                if (safeReach[i])
                    safe++;
            }
            if (walkable == 0 || safe / (float)walkable < MinSafeFraction)
                result.Errors.Add($"safe reachable area too small ({safe}/{walkable})");
            return result;
        }

        private static int CountSafeAround(ArenaMap map, TilePoint p, int radius)
        {
            int n = 0;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (map.IsSafe(p.X + dx, p.Y + dy))
                        n++;
                }
            }
            return n;
        }

        public static bool[] Flood(ArenaMap map, TilePoint start, bool safeOnly)
        {
            var reach = new bool[map.Width * map.Height];
            bool Passable(int x, int y) => safeOnly ? map.IsSafe(x, y) : map.IsWalkable(x, y);
            if (!map.InBounds(start.X, start.Y) || !Passable(start.X, start.Y))
                return reach;
            var queue = new Queue<TilePoint>();
            queue.Enqueue(start);
            reach[start.Y * map.Width + start.X] = true;
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = p.X + dx;
                    int ny = p.Y + dy;
                    if (!map.InBounds(nx, ny) || !Passable(nx, ny))
                        continue;
                    int idx = ny * map.Width + nx;
                    if (reach[idx])
                        continue;
                    reach[idx] = true;
                    queue.Enqueue(new TilePoint(nx, ny));
                }
            }
            return reach;
        }
    }
}
