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
            Assert.Equal("boss_forest_p1", director.DesiredMusic(RunPhase.InArena, world.State, null));
            var boss = world.State.Enemies.Single(e => e.IsBoss);
            boss.Hp = boss.MaxHp * 0.2f;
            world.Step();
            // a reconnecting client only has the state, not the phase-change event
            var replica = world.CaptureState();
            Assert.Equal("boss_forest_p3", director.DesiredMusic(RunPhase.InArena, replica, null));
            replica.PactActive = true;
            Assert.Equal("boss_forest_p3_pact", director.DesiredMusic(RunPhase.InArena, replica, null));
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
