using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Randomness;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Headless
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var data = GameData.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
            string mode = args.Length > 0 ? args[0] : "arena";
            switch (mode)
            {
                case "arena":
                    return RunArena(data, players: args.Length > 1 ? int.Parse(args[1]) : 2, boss: false, seed: args.Length > 2 ? ulong.Parse(args[2]) : 1, verbose: true);
                case "boss":
                    return RunArena(data, players: args.Length > 1 ? int.Parse(args[1]) : 2, boss: true, seed: args.Length > 2 ? ulong.Parse(args[2]) : 1, verbose: true);
                case "load":
                    return LoadTest(data, enemies: args.Length > 1 ? int.Parse(args[1]) : 32);
                default:
                    Console.Error.WriteLine("usage: headless [arena|boss] [players] [seed] | load [enemies]");
                    return 2;
            }
        }

        public static int RunArena(GameData data, int players, bool boss, ulong seed, bool verbose)
        {
            var setup = new ArenaSetup { RunId = "headless", Seed = seed, Level = boss ? 5 : 2 };
            if (boss)
            {
                setup.ArenaTemplateId = "forest_boss_warden";
                setup.Encounter = EncounterDirector.BossEncounter("forest_warden", players);
            }
            else
            {
                setup.ArenaTemplateId = "forest_clearing_01";
                setup.Encounter = new EncounterDirector(data).Build(EncounterKind.Waves, 1, setup.Level, "normal", players, false, new DeterministicRng(seed));
            }
            string[] weapons = { "warrior_sword", "warrior_axe", "warrior_mace" };
            for (int i = 0; i < players; i++)
            {
                setup.Players.Add(new PlayerLoadout
                {
                    ParticipantId = "bot" + i, DisplayName = "Bot " + i, ClassId = "warrior", WeaponTypeId = weapons[i % 3], Lives = 3, Level = boss ? 5 : 2,
                    EquippedAbilities = { "warrior_whirlwind", "warrior_shield_charge" },
                });
            }
            var world = new CombatWorld(data, WorldFactory.Create(data, setup));
            var bots = world.State.Players.Select((p, i) => (p.Id, Bot: new AutoPilot((ulong)i + seed))).ToList();
            var counts = new Dictionary<SimEventType, int>();
            var sw = Stopwatch.StartNew();
            int maxTicks = SimTime.Ticks(600f);
            while (world.State.Encounter.Status == EncounterStatus.InProgress && world.Tick < maxTicks)
            {
                var inputs = bots.ToDictionary(b => b.Id, b => b.Bot.Next(world.State, b.Id));
                world.Step(inputs);
                foreach (var e in world.Events)
                {
                    counts[e.Type] = counts.GetValueOrDefault(e.Type) + 1;
                    if (verbose && e.Type is SimEventType.WaveStarted or SimEventType.BossPhaseChanged or SimEventType.PlayerDowned or SimEventType.LifeLost
                        or SimEventType.PlayerRevived or SimEventType.BossStaggered or SimEventType.ArenaCleared or SimEventType.PartyDefeated or SimEventType.PlayerEliminated)
                        Console.WriteLine($"{SimTime.Seconds(e.Tick),7:0.0}s {e.Type} {e.Key} {e.IntValue}");
                }
                if (verbose && world.Tick % SimTime.Ticks(30f) == 0)
                {
                    var alive = world.State.Enemies.Where(e => e.Hp > 0).ToList();
                    Console.WriteLine($"{SimTime.Seconds(world.Tick),7:0.0}s enemies={alive.Count} [{string.Join(",", alive.Take(4).Select(e => $"{e.DefId}:{e.Hp:0}@{e.Position} {e.State}"))}] players=[{string.Join(",", world.State.Players.Select(p => $"{p.Hp:0}/{p.MaxHp:0} L{p.Lives} {p.LifeState}@{p.Position}"))}]");
                }
            }
            sw.Stop();
            Console.WriteLine($"result={world.State.Encounter.Status} simulated={SimTime.Seconds(world.Tick):0.0}s wall={sw.ElapsedMilliseconds}ms fallbackArena={world.State.Arena.UsedFallback}");
            Console.WriteLine(string.Join(" ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
            return world.State.Encounter.Status == EncounterStatus.Cleared ? 0 : 1;
        }

        /// <summary>Load test: 4 players, N simultaneous enemies, measures simulation cost per tick.</summary>
        public static int LoadTest(GameData data, int enemies)
        {
            var setup = new ArenaSetup { RunId = "load", Seed = 3, ArenaTemplateId = "forest_clearing_01", Encounter = new EncounterState { Kind = EncounterKind.Training } };
            for (int i = 0; i < 4; i++)
                setup.Players.Add(new PlayerLoadout { ParticipantId = "bot" + i, ClassId = "warrior", Lives = 3, EquippedAbilities = { "warrior_whirlwind", "warrior_shield_charge" } });
            var world = new CombatWorld(data, WorldFactory.Create(data, setup));
            foreach (var p in world.State.Players)
            {
                p.MaxHp = p.Hp = 1_000_000;
            }
            string[] kinds = { "forest_ghoul", "forest_thorn_archer", "forest_rot_shaman" };
            var spawns = world.State.Arena.SpawnPoints;
            for (int i = 0; i < enemies; i++)
            {
                var e = world.SpawnEnemy(kinds[i % kinds.Length], spawns[i % spawns.Count].Center, false);
                e.MaxHp = e.Hp = 1_000_000;
            }
            var bots = world.State.Players.Select((p, i) => (p.Id, Bot: new AutoPilot((ulong)i))).ToList();
            var times = new List<double>();
            int maxProjectiles = 0;
            for (int t = 0; t < SimTime.Ticks(60f); t++)
            {
                var inputs = bots.ToDictionary(b => b.Id, b => b.Bot.Next(world.State, b.Id));
                long start = Stopwatch.GetTimestamp();
                world.Step(inputs);
                times.Add((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
                maxProjectiles = Math.Max(maxProjectiles, world.State.Projectiles.Count);
            }
            times.Sort();
            double avg = times.Average();
            double p99 = times[(int)(times.Count * 0.99)];
            double max = times[^1];
            Console.WriteLine($"enemies={world.State.Enemies.Count} players=4 ticks={times.Count} avg={avg:0.000}ms p99={p99:0.000}ms max={max:0.000}ms maxProjectiles={maxProjectiles} budget={1000.0 / SimTime.TickRate:0.00}ms");
            return p99 < 4.0 ? 0 : 1;
        }
    }
}
