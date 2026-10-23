using System.Collections.Generic;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Net
{
    public sealed class LobbyMember
    {
        public string ParticipantId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ClassId { get; set; } = "warrior";
        public string WeaponTypeId { get; set; } = "";
        public bool Ready { get; set; }
        public bool IsHost { get; set; }
        public List<string> UnlockedClasses { get; set; } = new();
        public List<string> UnlockedDifficulties { get; set; } = new();
    }

    public sealed class LobbyView
    {
        public string DifficultyId { get; set; } = "normal";
        public List<LobbyMember> Members { get; set; } = new();
        public bool RunStarted { get; set; }
        public List<string> StartBlockers { get; set; } = new();
    }

    /// <summary>Sent reliably on join, reconnect and arena start.</summary>
    public sealed class FullStatePayload
    {
        public int YourPlayerEntityId { get; set; } = -1;
        public RunPhase Phase { get; set; }
        public WorldState? World { get; set; }
        public RunState? Run { get; set; }
        public bool IsReconnect { get; set; }
    }

    public sealed class RunNoticePayload
    {
        public RunPhase Phase { get; set; }
        public RunState? Run { get; set; }
        public List<string> PendingPactVoters { get; set; } = new();
        public string Message { get; set; } = "";
    }
}
