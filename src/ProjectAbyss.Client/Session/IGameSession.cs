using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Net;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Client.Session
{
    /// <summary>What the game screen needs, independent of whether this machine hosts or joined.</summary>
    public interface IGameSession : IDisposable
    {
        bool IsHost { get; }
        WorldState? World { get; }
        int OwnPlayerId { get; }
        string OwnParticipantId { get; }
        Vec2? OwnPositionOverride { get; }
        RunState? Run { get; }
        RunPhase? Phase { get; }
        LobbyView? Lobby { get; }
        IReadOnlyList<string> PendingPactVoters { get; }
        string? Problem { get; }
        void Tick(Vec2 move, float aim, InputButtons held);
        List<SimEvent> DrainEvents();
        void SetReady(bool ready);
        void SetClass(string classId);
        void SetWeapon(string weaponTypeId);
        void ContinueToNextArena();
        void VotePact(bool accept);
    }

    /// <summary>Single player and co-op host: the authoritative simulation runs in this process.</summary>
    public sealed class HostGameSession : IGameSession
    {
        private readonly List<SimEvent> events = new();
        private InputButtons lastHeld;
        private uint sequence;

        public HostGameSession(HostSession host, IDisposable? transport)
        {
            this.Host = host;
            this.Transport = transport;
        }

        public HostSession Host { get; }
        public IDisposable? Transport { get; }
        public bool IsHost => true;
        public WorldState? World => this.Host.Controller?.World?.State;
        public int OwnPlayerId => this.Host.Controller?.World?.FindPlayerByParticipant(this.Host.HostParticipantId)?.Id ?? -1;
        public string OwnParticipantId => this.Host.HostParticipantId;
        public Vec2? OwnPositionOverride => null;
        public RunState? Run => this.Host.Controller?.Run;
        public RunPhase? Phase => this.Host.Controller?.Phase;
        public LobbyView? Lobby => this.Host.Lobby;
        public IReadOnlyList<string> PendingPactVoters => this.Host.Controller?.PendingPactVoters().ToList() ?? new List<string>();
        public string? Problem => null;

        public void Tick(Vec2 move, float aim, InputButtons held)
        {
            var pressed = held & ~this.lastHeld;
            this.lastHeld = held;
            this.Host.SetHostInput(PlayerInput.Create(++this.sequence, move, aim, held, pressed));
            this.Host.Tick();
            this.events.AddRange(this.Host.LastTickEvents);
        }

        public List<SimEvent> DrainEvents()
        {
            var copy = new List<SimEvent>(this.events);
            this.events.Clear();
            return copy;
        }

        public void SetReady(bool ready) => this.Host.SetHostReady(ready);
        public void SetClass(string classId) => this.Host.SetHostClass(classId);
        public void SetWeapon(string weaponTypeId) => this.Host.SetHostWeapon(weaponTypeId);

        public void ContinueToNextArena()
        {
            if (this.Host.Controller?.Phase == RunPhase.BetweenArenas)
                this.Host.BeginArena();
        }

        public void VotePact(bool accept) => this.Host.HostVotePact(accept);

        public void Dispose() => this.Transport?.Dispose();
    }

    /// <summary>Co-op client: renders the host's replicated state, predicts only its own movement.</summary>
    public sealed class ClientGameSession : IGameSession
    {
        public ClientGameSession(ClientSession client, IDisposable? transport, string ownParticipantId)
        {
            this.Client = client;
            this.Transport = transport;
            this.OwnParticipantId = ownParticipantId;
        }

        public ClientSession Client { get; }
        public IDisposable? Transport { get; }
        public bool IsHost => false;
        public WorldState? World => this.Client.World;
        public int OwnPlayerId => this.Client.PlayerEntityId;
        public string OwnParticipantId { get; }
        public Vec2? OwnPositionOverride => this.Client.World != null ? this.Client.PredictedPosition : null;
        public RunState? Run => this.Client.Run;
        public RunPhase? Phase => this.Client.Stage == ClientStage.InRun ? this.Client.Phase : null;
        public LobbyView? Lobby => this.Client.Lobby;
        public IReadOnlyList<string> PendingPactVoters => this.Client.PendingPactVoters;

        public string? Problem => this.Client.Stage switch
        {
            ClientStage.Rejected => "Abgelehnt: " + this.Client.RejectReason,
            ClientStage.Disconnected => "Verbindung zum Host getrennt",
            _ => null,
        };

        public void Tick(Vec2 move, float aim, InputButtons held) => this.Client.Tick(move, aim, held);

        public List<SimEvent> DrainEvents()
        {
            var list = new List<SimEvent>(this.Client.ReceivedEvents);
            this.Client.ReceivedEvents.Clear();
            return list;
        }

        public void SetReady(bool ready) => this.Client.SendAction(ClientAction.SetReady, flag: ready);
        public void SetClass(string classId) => this.Client.SendAction(ClientAction.SetClass, classId);
        public void SetWeapon(string weaponTypeId) => this.Client.SendAction(ClientAction.SetWeapon, weaponTypeId);
        public void ContinueToNextArena()
        {
        }

        public void VotePact(bool accept) => this.Client.SendAction(ClientAction.PactVote, flag: accept);

        public void Dispose() => this.Transport?.Dispose();
    }
}
