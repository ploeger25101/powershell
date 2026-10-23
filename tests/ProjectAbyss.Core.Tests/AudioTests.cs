using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Audio;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;
using Xunit;

namespace ProjectAbyss.Core.Tests
{
    public class AudioTests
    {
        [Fact]
        public void BossMusicFollowsPhaseFromState()
        {
            var setup = new ArenaSetup { RunId = "t", Seed = 3, ArenaTemplateId = "forest_boss_warden", Encounter = EncounterDirector.BossEncounter("forest_warden", 1), Players = { TestData.Warrior("p0") } };
            var world = new CombatWorld(TestData.Data, WorldFactory.Create(TestData.Data, setup));
            world.Step();
            var director = new AudioDirector(TestData.Data);
            Assert.Equal("abyss_music_boss_forest_p1", director.DesiredMusic(RunPhase.InArena, world.State, null));
            var boss = world.State.Enemies.Single(e => e.IsBoss);
            boss.Hp = boss.MaxHp * 0.2f;
            world.Step();
            // a reconnecting client only has the state, not the phase-change event
            var replica = world.CaptureState();
            Assert.Equal("abyss_music_boss_forest_p3", director.DesiredMusic(RunPhase.InArena, replica, null));
            replica.PactActive = true;
            Assert.Equal("abyss_music_boss_forest_p3_pact", director.DesiredMusic(RunPhase.InArena, replica, null));
            Assert.Equal(AudioDirector.MenuMusic, director.DesiredMusic(null, null, null));
        }

        [Fact]
        public void IdenticalSoundsAreLimitedButWarningsAlwaysPlay()
        {
            var director = new AudioDirector(TestData.Data);
            var events = Enumerable.Range(0, 10).Select(i => new SimEvent { Type = SimEventType.Hit, TargetId = i }).ToList();
            events.Add(new SimEvent { Type = SimEventType.Telegraph, Key = "b:forest_warden:root_slam" });
            var sounds = director.SoundsFor(events, 100, 1);
            Assert.Equal(AudioDirector.MaxSameCue, sounds.Count(s => s.CueId == "abyss_sfx_hit"));
            Assert.Equal("abyss_sfx_boss_warning", sounds[0].CueId);
            var next = director.SoundsFor(events, 102, 1);
            Assert.DoesNotContain(next, s => s.CueId == "abyss_sfx_hit");
            Assert.Contains(next, s => s.CueId == "abyss_sfx_boss_warning");
        }
    }
}

namespace ProjectAbyss.Core.Tests
{
    public class AudioAssetTests
    {
        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "ProjectAbyss.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new System.InvalidOperationException("repository root not found");
        }

        [Fact]
        public void EveryCueTheGameRequestsHasAnAudioFile()
        {
            string audio = System.IO.Path.Combine(RepoRoot(), "src", "ProjectAbyss.Smapi", "assets", "audio");
            var files = System.IO.Directory.GetFiles(audio, "*", System.IO.SearchOption.AllDirectories)
                .Select(System.IO.Path.GetFileNameWithoutExtension).ToHashSet();
            var data = TestData.Data;
            var cues = new List<string>
            {
                AudioDirector.MenuMusic, AudioDirector.MerchantMusic, AudioDirector.VictoryMusic, AudioDirector.DefeatMusic, AudioDirector.PactMusic,
                "abyss_music_biome1_calm", "abyss_music_biome1_combat", "abyss_music_biome1_elite",
                "abyss_sfx_ability", "abyss_sfx_boss_warning", "abyss_sfx_telegraph", "abyss_sfx_hit", "abyss_sfx_crit", "abyss_sfx_player_hit",
                "abyss_sfx_parry", "abyss_sfx_dash", "abyss_sfx_enemy_die", "abyss_sfx_boss_die", "abyss_sfx_potion", "abyss_sfx_downed",
                "abyss_sfx_revive", "abyss_sfx_stagger", "abyss_sfx_boss_phase", "abyss_sfx_clear", "abyss_sfx_ui_click",
            };
            cues.AddRange(data.WeaponTypes.Keys.Select(w => "abyss_sfx_" + w));
            foreach (var boss in data.Bosses.Values)
            {
                cues.AddRange(boss.Phases.Select(p => p.Music));
                cues.AddRange(boss.Phases.Select(p => p.Music + "_pact"));
            }
            var missing = cues.Where(c => !files.Contains(c)).ToList();
            Assert.True(missing.Count == 0, "missing audio: " + string.Join(", ", missing));
        }
    }
}
