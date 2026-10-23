using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Audio
{
    public readonly record struct SoundRequest(string CueId, int Priority, float Volume);

    /// <summary>
    /// Decides music and sound effects from game state. Music is derived from state (not from events), so a client that
    /// reconnects mid-boss-fight immediately picks the track of the current boss phase.
    /// </summary>
    public sealed class AudioDirector
    {
        public const string MenuMusic = "abyss_music_menu";
        public const string MerchantMusic = "abyss_music_merchant";
        public const string VictoryMusic = "abyss_music_victory";
        public const string DefeatMusic = "abyss_music_defeat";
        public const string PactMusic = "abyss_music_pact";
        public const int WarningPriority = 100;

        /// <summary>At most this many instances of the same cue within <see cref="DuplicateWindowTicks"/>.</summary>
        public const int MaxSameCue = 3;
        public const int DuplicateWindowTicks = 6;
        public const int MaxSoundsPerTick = 8;

        private readonly GameData data;
        private readonly Dictionary<string, List<int>> recent = new();

        public AudioDirector(GameData data)
        {
            this.data = data;
        }

        public string DesiredMusic(RunPhase? phase, WorldState? world, RunState? run)
        {
            if (phase == null)
                return MenuMusic;
            if (phase == RunPhase.PactVote)
                return PactMusic;
            if (phase == RunPhase.Finished)
                return run?.Status == RunStatus.Won ? VictoryMusic : DefeatMusic;
            int level = run?.CurrentLevel ?? world?.Level ?? 1;
            int biome = RunPlan.BiomeOf(level);
            if (phase == RunPhase.BetweenArenas)
                return RunPlan.HasMerchant(level - 1) ? MerchantMusic : $"abyss_music_biome{biome}_calm";
            if (world == null)
                return $"abyss_music_biome{biome}_calm";
            var boss = world.Enemies.FirstOrDefault(e => e.IsBoss && e.Hp > 0);
            if (boss?.Boss != null && this.data.Bosses.TryGetValue(boss.DefId, out var def))
            {
                string phaseMusic = def.Phases[System.Math.Min(boss.Boss.PhaseIndex, def.Phases.Count - 1)].Music;
                return world.PactActive ? phaseMusic + "_pact" : phaseMusic;
            }
            bool elite = world.Enemies.Any(e => e.IsElite && e.Hp > 0);
            if (world.Encounter.Status == EncounterStatus.Cleared)
                return $"abyss_music_biome{biome}_calm";
            return elite ? $"abyss_music_biome{biome}_elite" : $"abyss_music_biome{biome}_combat";
        }

        /// <summary>Sound requests for one tick of events, prioritised and rate-limited.</summary>
        public List<SoundRequest> SoundsFor(IEnumerable<SimEvent> events, int tick, int ownPlayerId)
        {
            var list = new List<SoundRequest>();
            foreach (var e in events)
            {
                var req = this.Map(e, ownPlayerId);
                if (req != null)
                    list.Add(req.Value);
            }
            var result = new List<SoundRequest>();
            foreach (var r in list.OrderByDescending(r => r.Priority))
            {
                if (result.Count >= MaxSoundsPerTick && r.Priority < WarningPriority)
                    break;
                if (!this.recent.TryGetValue(r.CueId, out var times))
                    this.recent[r.CueId] = times = new List<int>();
                times.RemoveAll(t => tick - t > DuplicateWindowTicks);
                if (times.Count >= MaxSameCue && r.Priority < WarningPriority)
                    continue;
                times.Add(tick);
                result.Add(r);
            }
            return result;
        }

        private SoundRequest? Map(SimEvent e, int ownPlayerId)
        {
            switch (e.Type)
            {
                case SimEventType.ActionStarted when e.Key.StartsWith("w:", System.StringComparison.Ordinal):
                {
                    string weapon = e.Key.Split(':')[1];
                    return new SoundRequest($"abyss_sfx_{weapon}", 20, e.SourceId == ownPlayerId ? 1f : 0.6f);
                }
                case SimEventType.ActionStarted when e.Key.StartsWith("a:", System.StringComparison.Ordinal):
                    return new SoundRequest("abyss_sfx_ability", 30, 1f);
                case SimEventType.Telegraph when e.Key.StartsWith("b:", System.StringComparison.Ordinal):
                    return new SoundRequest("abyss_sfx_boss_warning", WarningPriority, 1f);
                case SimEventType.Telegraph:
                    return new SoundRequest("abyss_sfx_telegraph", 40, 0.7f);
                case SimEventType.Hit:
                    return new SoundRequest(e.Flag ? "abyss_sfx_crit" : "abyss_sfx_hit", e.Flag ? 35 : 25, 0.8f);
                case SimEventType.PlayerHit when e.TargetId == ownPlayerId:
                    return new SoundRequest("abyss_sfx_player_hit", 60, 1f);
                case SimEventType.Parried:
                    return new SoundRequest("abyss_sfx_parry", 70, 1f);
                case SimEventType.Dash when e.SourceId == ownPlayerId:
                    return new SoundRequest("abyss_sfx_dash", 30, 0.8f);
                case SimEventType.EnemyKilled:
                    return new SoundRequest(e.Flag ? "abyss_sfx_boss_die" : "abyss_sfx_enemy_die", e.Flag ? 90 : 25, 0.8f);
                case SimEventType.PotionUsed:
                    return new SoundRequest("abyss_sfx_potion", 50, 1f);
                case SimEventType.PlayerDowned:
                    return new SoundRequest("abyss_sfx_downed", 80, 1f);
                case SimEventType.PlayerRevived or SimEventType.PlayerRespawned:
                    return new SoundRequest("abyss_sfx_revive", 70, 1f);
                case SimEventType.BossStaggered:
                    return new SoundRequest("abyss_sfx_stagger", 85, 1f);
                case SimEventType.BossPhaseChanged:
                    return new SoundRequest("abyss_sfx_boss_phase", WarningPriority, 1f);
                case SimEventType.ArenaCleared:
                    return new SoundRequest("abyss_sfx_clear", 90, 1f);
                default:
                    return null;
            }
        }
    }
}
