using System;
using System.Collections.Generic;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Core.Arena
{
    public enum TileType : byte
    {
        Floor = 0,
        Wall = 1,
        /// <summary>Randomly placed blocking obstacle (rocks, roots, pillars).</summary>
        Obstacle = 2,
        /// <summary>Walkable but damaging (spikes, embers, frost vents).</summary>
        Hazard = 3,
    }

    public readonly record struct TilePoint(int X, int Y)
    {
        public Vec2 Center => new((this.X + 0.5f) * WorldUnits.TileSize, (this.Y + 0.5f) * WorldUnits.TileSize);
        public static TilePoint FromWorld(Vec2 p) => new((int)MathF.Floor(p.X / WorldUnits.TileSize), (int)MathF.Floor(p.Y / WorldUnits.TileSize));
    }

    /// <summary>A generated, validated arena. Serialisable (tiles are stored as a byte array).</summary>
    public sealed class ArenaMap
    {
        public string TemplateId { get; set; } = "";
        public ulong Seed { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Tiles { get; set; } = Array.Empty<byte>();
        public List<TilePoint> PlayerStarts { get; set; } = new();
        public List<TilePoint> SpawnPoints { get; set; } = new();
        public List<TilePoint> Exits { get; set; } = new();
        public TilePoint? BossSpawn { get; set; }
        public bool IsBossArena { get; set; }
        public bool UsedFallback { get; set; }

        public static ArenaMap Create(int width, int height)
        {
            return new ArenaMap { Width = width, Height = height, Tiles = new byte[width * height] };
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < this.Width && y < this.Height;

        public TileType Get(int x, int y) => this.InBounds(x, y) ? (TileType)this.Tiles[y * this.Width + x] : TileType.Wall;

        public void Set(int x, int y, TileType type) => this.Tiles[y * this.Width + x] = (byte)type;

        public bool IsSolid(int x, int y)
        {
            var t = this.Get(x, y);
            return t == TileType.Wall || t == TileType.Obstacle;
        }

        public bool IsWalkable(int x, int y) => !this.IsSolid(x, y);

        public bool IsSafe(int x, int y) => this.Get(x, y) == TileType.Floor;

        public float PixelWidth => this.Width * WorldUnits.TileSize;
        public float PixelHeight => this.Height * WorldUnits.TileSize;

        public bool CircleOverlapsSolid(Vec2 center, float radius)
        {
            int ts = WorldUnits.TileSize;
            int x0 = (int)MathF.Floor((center.X - radius) / ts);
            int x1 = (int)MathF.Floor((center.X + radius) / ts);
            int y0 = (int)MathF.Floor((center.Y - radius) / ts);
            int y1 = (int)MathF.Floor((center.Y + radius) / ts);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!this.IsSolid(x, y))
                        continue;
                    float cx = Math.Clamp(center.X, x * ts, (x + 1) * ts);
                    float cy = Math.Clamp(center.Y, y * ts, (y + 1) * ts);
                    float dx = center.X - cx;
                    float dy = center.Y - cy;
                    if (dx * dx + dy * dy < radius * radius)
                        return true;
                }
            }
            return false;
        }

        /// <summary>Move a circle by delta, sliding along walls. Sub-stepped so fast dashes cannot tunnel through tiles.</summary>
        public Vec2 MoveCircle(Vec2 position, Vec2 delta, float radius)
        {
            float len = delta.Length;
            if (len < 1e-5f)
                return position;
            int steps = Math.Max(1, (int)MathF.Ceiling(len / 3f));
            Vec2 step = delta / steps;
            Vec2 pos = position;
            for (int i = 0; i < steps; i++)
            {
                var tryX = new Vec2(pos.X + step.X, pos.Y);
                if (!this.CircleOverlapsSolid(tryX, radius))
                    pos = tryX;
                var tryY = new Vec2(pos.X, pos.Y + step.Y);
                if (!this.CircleOverlapsSolid(tryY, radius))
                    pos = tryY;
            }
            return pos;
        }

        /// <summary>
        /// Steering move for AI: tries the desired direction, then progressively rotated alternatives, so agents
        /// slide around obstacle corners instead of pushing into them forever.
        /// </summary>
        public Vec2 SteerCircle(Vec2 position, Vec2 direction, float distance, float radius)
        {
            if (distance <= 0 || direction.LengthSquared < 1e-6f)
                return position;
            float baseAngle = direction.Angle;
            Vec2 best = position;
            float bestProgress = 0f;
            foreach (float offset in SteerOffsets)
            {
                var dir = Vec2.FromAngle(baseAngle + offset);
                var moved = this.MoveCircle(position, dir * distance, radius);
                // progress measured along the originally desired direction, penalising large detours
                float progress = Vec2.Distance(moved, position) * (offset == 0f ? 1f : 0.85f);
                if (progress >= distance * 0.7f)
                    return moved;
                if (progress > bestProgress)
                {
                    bestProgress = progress;
                    best = moved;
                }
            }
            return best;
        }

        private static readonly float[] SteerOffsets = { 0f, 0.6f, -0.6f, 1.1f, -1.1f, 1.57f, -1.57f };

        /// <summary>Whether a circle of the given radius can travel in a straight line between two points.</summary>
        public bool HasClearPath(Vec2 from, Vec2 to, float radius)
        {
            float dist = Vec2.Distance(from, to);
            int samples = Math.Max(1, (int)(dist / 4f));
            for (int i = 1; i <= samples; i++)
            {
                if (this.CircleOverlapsSolid(from + (to - from) * (i / (float)samples), radius))
                    return false;
            }
            return true;
        }

        /// <summary>Line-of-sight check by sampling along the segment (good enough for 16 px tiles).</summary>
        public bool HasLineOfSight(Vec2 from, Vec2 to)
        {
            float dist = Vec2.Distance(from, to);
            int samples = Math.Max(1, (int)(dist / 6f));
            for (int i = 1; i < samples; i++)
            {
                var p = from + (to - from) * (i / (float)samples);
                var t = TilePoint.FromWorld(p);
                if (this.IsSolid(t.X, t.Y))
                    return false;
            }
            return true;
        }
    }
}
