using System;
using System.Collections.Generic;
using System.IO;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Tests
{
    internal static class TestData
    {
        private static readonly Lazy<GameData> Lazy = new(() => GameData.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data")));

        public static GameData Data => Lazy.Value;

        public static PlayerLoadout Warrior(string id, string weapon = "warrior_sword") => new()
        {
            ParticipantId = id,
            DisplayName = id,
            ClassId = "warrior",
            WeaponTypeId = weapon,
            Lives = 3,
            EquippedAbilities = new List<string> { "warrior_whirlwind", "warrior_shield_charge" },
        };

        public static CombatWorld EmptyArena(int players = 1, string template = "forest_fallback")
        {
            var setup = new ArenaSetup
            {
                RunId = "test",
                Seed = 42,
                ArenaTemplateId = template,
                Encounter = new EncounterState { Kind = EncounterKind.Training },
            };
            for (int i = 0; i < players; i++)
                setup.Players.Add(Warrior("p" + i));
            var world = new CombatWorld(Data, WorldFactory.Create(Data, setup));
            return world;
        }

        public static void Run(CombatWorld world, int ticks, Func<int, Dictionary<int, Simulation.PlayerInput>>? inputs = null)
        {
            for (int i = 0; i < ticks; i++)
                world.Step(inputs?.Invoke(i));
        }
    }
}
