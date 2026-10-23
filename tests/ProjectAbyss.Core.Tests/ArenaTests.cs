using System.Linq;
using ProjectAbyss.Core.Arena;
using ProjectAbyss.Core.Randomness;
using Xunit;
using Xunit.Abstractions;

namespace ProjectAbyss.Core.Tests
{
    public class ArenaTests
    {
        private readonly ITestOutputHelper output;

        public ArenaTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void TenThousandGeneratedArenasAreAllValid()
        {
            var gen = new ArenaGenerator(TestData.Data);
            int fallbacks = 0;
            foreach (var template in TestData.Data.Arenas.Values.Where(a => !a.IsFallback))
            {
                for (ulong seed = 0; seed < 10_000; seed++)
                {
                    var map = gen.Generate(template.Id, DeterministicRng.Mix(seed, 77));
                    var result = ArenaValidator.Validate(map);
                    Assert.True(result.IsValid, $"{template.Id} seed {seed}: {string.Join("; ", result.Errors)}");
                    if (map.UsedFallback)
                        fallbacks++;
                }
            }
            this.output.WriteLine($"fallback arenas used: {fallbacks}");
        }

        [Fact]
        public void GenerationIsDeterministicPerSeed()
        {
            var gen = new ArenaGenerator(TestData.Data);
            var a = gen.Generate("forest_clearing_01", 5);
            var b = gen.Generate("forest_clearing_01", 5);
            var c = gen.Generate("forest_clearing_01", 6);
            Assert.Equal(a.Tiles, b.Tiles);
            Assert.NotEqual(a.Tiles, c.Tiles);
        }

        [Fact]
        public void ValidatorRejectsHazardAtStartAndUnreachableExit()
        {
            var template = TestData.Data.Arenas["forest_fallback"];
            var map = ArenaGenerator.Build(template, 1, randomSlots: false);
            Assert.True(ArenaValidator.Validate(map).IsValid);

            var start = map.PlayerStarts[0];
            map.Set(start.X + 1, start.Y, TileType.Hazard);
            Assert.Contains(ArenaValidator.Validate(map).Errors, e => e.Contains("hazard adjacent"));

            var map2 = ArenaGenerator.Build(template, 1, randomSlots: false);
            var exit = map2.Exits[0];
            for (int y = 0; y < map2.Height; y++)
                map2.Set(exit.X - 1, y, TileType.Wall);
            Assert.Contains(ArenaValidator.Validate(map2).Errors, e => e.Contains("exit"));
        }

        [Fact]
        public void InvalidTemplateFallsBackToSafeArena()
        {
            var bad = new Data.ArenaTemplateDef
            {
                Id = "bad",
                Rows = new() { "#####", "#P.E#", "#####" }, // no spawns, no room to dodge
            };
            Assert.False(ArenaValidator.Validate(ArenaGenerator.Build(bad, 1)).IsValid);
            var fb = new ArenaGenerator(TestData.Data).BuildFallback(false, 1);
            Assert.True(fb.UsedFallback);
            Assert.True(ArenaValidator.Validate(fb).IsValid);
        }

        [Fact]
        public void MovementCannotTunnelThroughWalls()
        {
            var map = ArenaGenerator.Build(TestData.Data.Arenas["forest_fallback"], 1, randomSlots: false);
            var start = map.PlayerStarts[0].Center;
            var moved = map.MoveCircle(start, new Mathematics.Vec2(-500, 0), 7f);
            Assert.True(moved.X >= 16 + 7 - 0.01f, $"x={moved.X}");
        }
    }
}
