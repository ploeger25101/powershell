using System;

namespace ProjectAbyss.Core.Mathematics
{
    /// <summary>The eight animation facings. Only used to pick sprites; hit detection always uses the exact aim angle.</summary>
    public enum Direction8 : byte
    {
        Up = 0,
        UpRight = 1,
        Right = 2,
        DownRight = 3,
        Down = 4,
        DownLeft = 5,
        Left = 6,
        UpLeft = 7,
    }

    public static class Direction8Util
    {
        /// <summary>Pick the facing whose 45° sector contains the given angle (radians, 0 = right, CCW = up).</summary>
        public static Direction8 FromAngle(float radians)
        {
            // Convert to "clockwise from up" so index 0 = Up, 2 = Right, 4 = Down.
            float deg = radians * 180f / MathF.PI;
            float fromUpClockwise = 90f - deg;
            fromUpClockwise %= 360f;
            if (fromUpClockwise < 0)
                fromUpClockwise += 360f;
            int index = (int)MathF.Floor((fromUpClockwise + 22.5f) / 45f) % 8;
            return (Direction8)index;
        }

        public static Direction8 FromVector(Vec2 v, Direction8 fallback)
        {
            return v.LengthSquared < 1e-6f ? fallback : FromAngle(v.Angle);
        }

        public static Vec2 ToUnitVector(Direction8 dir)
        {
            float fromUpClockwise = (int)dir * 45f;
            float radians = (90f - fromUpClockwise) * MathF.PI / 180f;
            return Vec2.FromAngle(radians);
        }
    }
}
