using System;

namespace ProjectAbyss.Core.Mathematics
{
    public static class Geometry
    {
        /// <summary>Smallest signed difference between two angles in radians, in [-π, π].</summary>
        public static float AngleDelta(float a, float b)
        {
            float d = (b - a) % (2 * MathF.PI);
            if (d > MathF.PI)
                d -= 2 * MathF.PI;
            else if (d < -MathF.PI)
                d += 2 * MathF.PI;
            return d;
        }

        public static bool CircleIntersectsCircle(Vec2 a, float ra, Vec2 b, float rb)
        {
            float r = ra + rb;
            return Vec2.DistanceSquared(a, b) <= r * r;
        }

        /// <summary>Whether a target circle overlaps a circular sector (cone) defined by origin, facing angle, half-angle and radius.</summary>
        public static bool SectorIntersectsCircle(Vec2 origin, float facing, float halfAngle, float radius, Vec2 target, float targetRadius)
        {
            Vec2 toTarget = target - origin;
            float dist = toTarget.Length;
            if (dist > radius + targetRadius)
                return false;
            if (dist <= targetRadius)
                return true; // origin inside target
            float angleToTarget = toTarget.Angle;
            float delta = MathF.Abs(AngleDelta(facing, angleToTarget));
            // widen the cone by the angular size of the target so edge grazes count
            float angularRadius = MathF.Asin(MathF.Min(1f, targetRadius / dist));
            return delta <= halfAngle + angularRadius;
        }

        /// <summary>Whether a circle intersects a capsule (thick segment from a to b).</summary>
        public static bool CapsuleIntersectsCircle(Vec2 a, Vec2 b, float capsuleRadius, Vec2 c, float circleRadius)
        {
            float dist = DistancePointSegment(c, a, b);
            return dist <= capsuleRadius + circleRadius;
        }

        public static float DistancePointSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            Vec2 ab = b - a;
            float lenSq = ab.LengthSquared;
            if (lenSq < 1e-9f)
                return Vec2.Distance(p, a);
            float t = Math.Clamp(Vec2.Dot(p - a, ab) / lenSq, 0f, 1f);
            return Vec2.Distance(p, a + ab * t);
        }
    }
}
