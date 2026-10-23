using System;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Randomness;
using ProjectAbyss.Core.Simulation;
using Xunit;

namespace ProjectAbyss.Core.Tests
{
    public class MathTests
    {
        [Theory]
        [InlineData(0f, Direction8.Right)]
        [InlineData(45f, Direction8.UpRight)]
        [InlineData(90f, Direction8.Up)]
        [InlineData(135f, Direction8.UpLeft)]
        [InlineData(180f, Direction8.Left)]
        [InlineData(-135f, Direction8.DownLeft)]
        [InlineData(-90f, Direction8.Down)]
        [InlineData(-45f, Direction8.DownRight)]
        [InlineData(22f, Direction8.Right)]
        [InlineData(23f, Direction8.UpRight)]
        [InlineData(359f, Direction8.Right)]
        public void AngleMapsToNearestOfEightFacings(float degrees, Direction8 expected)
        {
            Assert.Equal(expected, Direction8Util.FromAngle(degrees * MathF.PI / 180f));
        }

        [Fact]
        public void ScreenDownIsDownFacing()
        {
            Assert.Equal(Direction8.Down, Direction8Util.FromVector(new Vec2(0, 1), Direction8.Up));
            Assert.Equal(Direction8.UpLeft, Direction8Util.FromVector(new Vec2(-1, -1), Direction8.Up));
        }

        [Fact]
        public void EveryFacingRoundTrips()
        {
            foreach (Direction8 d in Enum.GetValues(typeof(Direction8)))
                Assert.Equal(d, Direction8Util.FromVector(Direction8Util.ToUnitVector(d), Direction8.Up));
        }

        [Fact]
        public void SectorHitUsesExactAngleNotEightDirections()
        {
            // target at 10° off the aim, cone half-angle 12°: hit; at 20°: miss. Eight-way snapping could not distinguish these.
            var origin = Vec2.Zero;
            float aim = 0.3f;
            var hit = Vec2.FromAngle(aim + 10f * MathF.PI / 180f) * 30f;
            var miss = Vec2.FromAngle(aim + 20f * MathF.PI / 180f) * 30f;
            Assert.True(Geometry.SectorIntersectsCircle(origin, aim, 12f * MathF.PI / 180f, 40f, hit, 0.1f));
            Assert.False(Geometry.SectorIntersectsCircle(origin, aim, 12f * MathF.PI / 180f, 40f, miss, 0.1f));
        }

        [Fact]
        public void RngIsReproducibleAndResumable()
        {
            var a = new DeterministicRng(1234);
            for (int i = 0; i < 17; i++)
                a.NextUInt();
            var saved = a.SaveState();
            uint expected = a.NextUInt();
            var b = DeterministicRng.FromState(saved);
            Assert.Equal(expected, b.NextUInt());
            Assert.Equal(new DeterministicRng(99).NextUInt(), new DeterministicRng(99).NextUInt());
            Assert.NotEqual(new DeterministicRng(99).NextUInt(), new DeterministicRng(100).NextUInt());
        }

        [Fact]
        public void RngIntRangeIsUnbiasedEnough()
        {
            var rng = new DeterministicRng(7);
            var counts = new int[6];
            for (int i = 0; i < 60000; i++)
                counts[rng.NextInt(0, 6)]++;
            foreach (int c in counts)
                Assert.InRange(c, 9500, 10500);
        }

        [Fact]
        public void InputQuantisationKeepsAimPrecise()
        {
            for (float a = -3f; a < 3f; a += 0.37f)
            {
                var input = PlayerInput.Create(1, new Vec2(1, 0), a, InputButtons.None, InputButtons.None);
                float back = input.AimRadians;
                Assert.True(MathF.Abs(Geometry.AngleDelta(a, back)) < 0.001f);
            }
        }
    }
}
