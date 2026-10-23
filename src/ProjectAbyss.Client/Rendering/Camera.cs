using System;
using Microsoft.Xna.Framework;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Client.Rendering
{
    /// <summary>Integer-scaled camera so pixel art stays crisp; follows a target and clamps to the arena.</summary>
    public sealed class Camera
    {
        public int Scale { get; private set; } = 3;
        public Vector2 Offset { get; private set; }
        public Vector2 Shake { get; set; }

        public void Update(Point screen, float arenaWidth, float arenaHeight, Vec2 follow, float hudScale)
        {
            // fit at least ~22 tiles vertically; integer scale only
            int byHeight = (int)Math.Floor(screen.Y / Math.Min(arenaHeight + 32f, 22f * 16f));
            this.Scale = Math.Clamp(byHeight, 1, 8);
            float viewW = screen.X / (float)this.Scale;
            float viewH = screen.Y / (float)this.Scale;
            float x = arenaWidth <= viewW ? (arenaWidth - viewW) / 2f : Math.Clamp(follow.X - viewW / 2f, 0, arenaWidth - viewW);
            float y = arenaHeight <= viewH ? (arenaHeight - viewH) / 2f : Math.Clamp(follow.Y - viewH / 2f, 0, arenaHeight - viewH);
            this.Offset = new Vector2(MathF.Round(x * this.Scale), MathF.Round(y * this.Scale));
        }

        public Vector2 ToScreen(Vec2 world) => new Vector2(world.X * this.Scale, world.Y * this.Scale) - this.Offset + this.Shake;

        public Vec2 ToWorld(Point screen) => new((screen.X + this.Offset.X - this.Shake.X) / this.Scale, (screen.Y + this.Offset.Y - this.Shake.Y) / this.Scale);
    }
}
