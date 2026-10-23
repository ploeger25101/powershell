namespace ProjectAbyss.Core.Run
{
    /// <summary>Fixed structure of a run: 25 levels in 5 biomes, bosses on every 5th level, merchants on 4/9/14/19/24.</summary>
    public static class RunPlan
    {
        public const int TotalLevels = 25;
        public const int LevelsPerBiome = 5;

        public static int BiomeOf(int level) => (level - 1) / LevelsPerBiome + 1;
        public static bool IsBossLevel(int level) => level % LevelsPerBiome == 0;
        public static bool HasMerchant(int level) => level % LevelsPerBiome == 4;
        public static bool IsFinalLevel(int level) => level == TotalLevels;
    }
}
