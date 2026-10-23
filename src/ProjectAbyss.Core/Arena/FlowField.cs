using System;
using System.Collections.Generic;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Arena
{
    /// <summary>
    /// Multi-source BFS distance field over walkable tiles. One field towards all living players is shared by
    /// every chasing enemy, so pathing cost does not grow with enemy count.
    /// </summary>
    public sealed class FlowField
    {
        public const int Unreachable = int.MaxValue;
        private static readonly (int dx, int dy)[] Neighbours =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
        };

        private readonly ArenaMap map;
        private readonly int[] distance;
        private readonly Queue<int> queue = new();

        public FlowField(ArenaMap map)
        {
            this.map = map;
            this.distance = new int[map.Width * map.Height];
        }

        public void Build(IEnumerable<TilePoint> sources)
        {
            Array.Fill(this.distance, Unreachable);
            this.queue.Clear();
            foreach (var s in sources)
            {
                if (!this.map.InBounds(s.X, s.Y) || this.map.IsSolid(s.X, s.Y))
                    continue;
                int idx = s.Y * this.map.Width + s.X;
                if (this.distance[idx] == 0)
                    continue;
                this.distance[idx] = 0;
                this.queue.Enqueue(idx);
            }
            while (this.queue.Count > 0)
            {
                int idx = this.queue.Dequeue();
                int x = idx % this.map.Width;
                int y = idx / this.map.Width;
                int d = this.distance[idx];
                foreach (var (dx, dy) in Neighbours)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (!this.map.InBounds(nx, ny) || this.map.IsSolid(nx, ny))
                        continue;
                    // no diagonal corner cutting
                    if (dx != 0 && dy != 0 && (this.map.IsSolid(x + dx, y) || this.map.IsSolid(x, y + dy)))
                        continue;
                    int nIdx = ny * this.map.Width + nx;
                    if (this.distance[nIdx] != Unreachable)
                        continue;
                    this.distance[nIdx] = d + 1;
                    this.queue.Enqueue(nIdx);
                }
            }
        }

        public int DistanceAt(int x, int y) => this.map.InBounds(x, y) ? this.distance[y * this.map.Width + x] : Unreachable;

        /// <summary>Direction towards the neighbouring tile with the smallest distance, or zero if none is better.</summary>
        public Vec2 DirectionFrom(Vec2 worldPos)
        {
            var t = TilePoint.FromWorld(worldPos);
            int best = this.DistanceAt(t.X, t.Y);
            TilePoint? bestTile = null;
            foreach (var (dx, dy) in Neighbours)
            {
                int nx = t.X + dx;
                int ny = t.Y + dy;
                if (dx != 0 && dy != 0 && (this.map.IsSolid(t.X + dx, t.Y) || this.map.IsSolid(t.X, t.Y + dy)))
                    continue;
                int d = this.DistanceAt(nx, ny);
                if (d < best)
                {
                    best = d;
                    bestTile = new TilePoint(nx, ny);
                }
            }
            return bestTile == null ? Vec2.Zero : (bestTile.Value.Center - worldPos).Normalized();
        }
    }
}
