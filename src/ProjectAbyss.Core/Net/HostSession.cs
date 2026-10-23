using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Net
{
    /// <summary>
    /// Authoritative host. Owns the run, simulates combat, validates every client request and sends compact snapshots.
    /// Clients can only send inputs and action requests.
    /// </summary>
    public sealed class HostSession
    {
        private enum Stage
        {
            AwaitingHello,
            AwaitingChallenge,
            AwaitingFullStateAck,
            Active,
        }

        private sealed class Peer
        {
            public long Handle;
            public Stage Stage;
            public byte[] Nonce = Array.Empty<byte>();
            public HelloMessage? Hello;
            public string ParticipantId = "";
            public int LastPacketTick;
            public bool PendingProtection;
            public readonly SortedDictionary<uint, PlayerInput> Inputs = new();
            public PlayerInput LastInput;
            public readonly Dictionary<uint, (bool ok, string reason)> HandledRequests = new();
        }

        private readonly INetTransport transport;
        private readonly GameData data;
        private readonly ContentIndex index;
        private readonly SnapshotCodec codec;
        private readonly Func<RunState, RunStore?> storeFactory;
        private readonly Dictionary<long, Peer> peers = new();
        private readonly List<SimEvent> pendingEvents = new();
        private readonly string assetHash;
        private PlayerInput hostInput;
        private int sessionTick;

        public HostSession(INetTransport transport, GameData data, string hostParticipantId, string hostName, IEnumerable<string> hostClasses, IEnumerable<string> hostDifficulties,
            string assetHash, Func<RunState, RunStore?> storeFactory)
        {
            this.transport = transport;
            this.data = data;
            this.index = new ContentIndex(data);
            this.codec = new SnapshotCodec(this.index);
            this.assetHash = assetHash;
            this.storeFactory = storeFactory;
            this.HostParticipantId = hostParticipantId;
            this.Lobby.Members.Add(new LobbyMember
            {
                ParticipantId = hostParticipantId,
                DisplayName = Sanitize(hostName),
                IsHost = true,
                ClassId = data.Classes.Keys.First(),
                UnlockedClasses = hostClasses.ToList(),
                UnlockedDifficulties = hostDifficulties.ToList(),
            });
        }

        public string HostParticipantId { get; }
        public LobbyView Lobby { get; } = new();
        public RunController? Controller { get; private set; }
        public int ConnectedClients => this.peers.Values.Count(p => p.Stage == Stage.Active);
        public string GameVersion { get; set; } = EnvelopeIO.GameVersion;
        public float TimeoutSeconds => this.data.Balance.Reconnect.TimeoutSeconds;

        public event Action<string>? Log;

        /// <summary>Simulation events of the most recent tick (for the host's own presentation).</summary>
        public IReadOnlyList<SimEvent> LastTickEvents { get; private set; } = Array.Empty<SimEvent>();

        public static string Sanitize(string name)
        {
            var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length > Protocol.MaxNameLength)
                clean = clean.Substring(0, Protocol.MaxNameLength);
            return clean.Length == 0 ? "Spieler" : clean;
        }

        // ---------------------------------------------------------------- lobby (host UI)

        public bool SetHostClass(string classId) => this.SetClass(this.HostParticipantId, classId);

        public bool SetHostWeapon(string weaponTypeId) => this.SetWeapon(this.HostParticipantId, weaponTypeId);

        private bool SetWeapon(string participantId, string weaponTypeId)
        {
            var m = this.Lobby.Members.Find(x => x.ParticipantId == participantId);
            if (m == null || this.Lobby.RunStarted || !this.data.WeaponTypes.TryGetValue(weaponTypeId, out var w) || w.ClassId != m.ClassId)
                return false;
            m.WeaponTypeId = weaponTypeId;
            this.BroadcastLobby();
            return true;
        }

        public bool SetDifficulty(string difficultyId)
        {
            if (this.Lobby.RunStarted || !this.data.Balance.Scaling.Difficulties.Any(d => d.Id == difficultyId))
                return false;
            this.Lobby.DifficultyId = difficultyId;
            this.BroadcastLobby();
            return true;
        }

        public void SetHostReady(bool ready)
        {
            this.Lobby.Members[0].Ready = ready;
            this.BroadcastLobby();
        }

        private bool SetClass(string participantId, string classId)
        {
            var m = this.Lobby.Members.Find(x => x.ParticipantId == participantId);
            if (m == null || this.Lobby.RunStarted || !this.data.Classes.ContainsKey(classId) || !m.UnlockedClasses.Contains(classId))
                return false;
            m.ClassId = classId;
            m.WeaponTypeId = this.data.Classes[classId].WeaponTypes[0];
            m.Ready = false;
            this.BroadcastLobby();
            return true;
        }

        /// <summary>Reasons the host cannot start yet (empty = can start).</summary>
        public List<string> StartBlockers()
        {
            var list = new List<string>();
            foreach (var m in this.Lobby.Members)
            {
                if (!m.Ready)
                    list.Add($"{m.DisplayName} ist nicht bereit");
                if (!m.UnlockedDifficulties.Contains(this.Lobby.DifficultyId))
                    list.Add($"{m.DisplayName} hat den Schwierigkeitsgrad nicht freigeschaltet");
                if (!m.UnlockedClasses.Contains(m.ClassId))
                    list.Add($"{m.DisplayName} hat die Klasse nicht freigeschaltet");
            }
            return list;
        }

        /// <param name="developerStartLevel">Developer mode only: start directly at this level (e.g. a boss level).</param>
        public RunController StartRun(ulong seed, bool developer, int developerStartLevel = 1)
        {
            var blockers = this.StartBlockers();
            if (blockers.Count > 0)
                throw new InvalidOperationException(string.Join("; ", blockers));
            string runId = Hashing.Sha256Hex(seed + ":" + DateTime.UtcNow.Ticks + ":" + this.HostParticipantId).Substring(0, 12);
            var run = RunController.CreateRun(this.data, runId, seed, this.Lobby.DifficultyId,
                this.Lobby.Members.Select(m => (m.ParticipantId, m.DisplayName, m.ClassId, m.WeaponTypeId)), developer);
            if (developer && developerStartLevel > 1)
                run.CurrentLevel = Math.Min(RunPlan.TotalLevels, developerStartLevel);
            this.Controller = new RunController(this.data, run, this.storeFactory(run));
            this.Controller.Log += s => this.Log?.Invoke(s);
            this.Lobby.RunStarted = true;
            this.BroadcastLobby();
            this.BeginArena();
            return this.Controller;
        }

        /// <summary>Continue a run after a host restart; only original participants may reconnect.</summary>
        public void ResumeRun(RecoveredRun recovered)
        {
            this.Controller = new RunController(this.data, recovered.Run, this.storeFactory(recovered.Run));
            this.Controller.Log += s => this.Log?.Invoke(s);
            this.Lobby.RunStarted = true;
            this.Lobby.Members.RemoveAll(m => !m.IsHost);
            if (recovered.World != null)
            {
                foreach (var p in recovered.World.Players.Where(p => p.ParticipantId != this.HostParticipantId))
                    p.Connected = false; // nobody is connected right after a host restart
                this.Controller.ResumeArena(recovered.World);
            }
        }

        public void BeginArena()
        {
            var c = this.Controller ?? throw new InvalidOperationException("No run.");
            var connected = new HashSet<string>(this.peers.Values.Where(p => p.Stage == Stage.Active).Select(p => p.ParticipantId)) { this.HostParticipantId };
            c.BeginArena(connected);
            foreach (var peer in this.peers.Values.Where(p => p.Stage == Stage.Active))
                this.SendFullState(peer, reconnect: false);
        }

        // ---------------------------------------------------------------- main loop

        public void SetHostInput(PlayerInput input) => this.hostInput = input;

        /// <summary>One fixed simulation tick (60 Hz).</summary>
        public void Tick()
        {
            this.sessionTick++;
            this.LastTickEvents = Array.Empty<SimEvent>();
            this.PumpTransport();
            this.CheckTimeouts();

            var c = this.Controller;
            if (c?.World != null && c.Phase == RunPhase.InArena)
            {
                var inputs = new Dictionary<int, PlayerInput>();
                var hostPlayer = c.World.FindPlayerByParticipant(this.HostParticipantId);
                if (hostPlayer != null)
                    inputs[hostPlayer.Id] = this.hostInput;
                foreach (var peer in this.peers.Values.Where(p => p.Stage == Stage.Active))
                {
                    var player = c.World.FindPlayerByParticipant(peer.ParticipantId);
                    if (player != null)
                        inputs[player.Id] = this.NextInput(peer, player);
                }
                var world = c.World;
                var events = c.Step(inputs);
                this.LastTickEvents = events.ToList();
                this.pendingEvents.AddRange(events);
                if (c.Phase != RunPhase.InArena)
                {
                    this.BroadcastNotice(c.Phase == RunPhase.PactVote ? "Pakt der letzten Chance: Abstimmung" : "Arena beendet");
                    this.pendingEvents.Clear();
                }
                else if (this.sessionTick % Protocol.SnapshotIntervalTicks == 0)
                {
                    foreach (var peer in this.peers.Values.Where(p => p.Stage == Stage.Active))
                    {
                        var player = world.FindPlayerByParticipant(peer.ParticipantId);
                        this.transport.Send(peer.Handle, this.codec.Encode(world.State, player?.LastProcessedInput ?? 0, this.pendingEvents), reliable: false);
                    }
                    this.pendingEvents.Clear();
                }
            }
        }

        /// <summary>
        /// One input per tick in sequence order. If the next input has not arrived, the last one is repeated without its
        /// button edges. A backlog is collapsed while keeping every press so no action is lost.
        /// </summary>
        private PlayerInput NextInput(Peer peer, PlayerEntity player)
        {
            while (peer.Inputs.Count > 6)
            {
                var first = peer.Inputs.First();
                peer.Inputs.Remove(first.Key);
                var next = peer.Inputs.First();
                peer.Inputs[next.Key] = new PlayerInput(next.Value.Sequence, next.Value.MoveX, next.Value.MoveY, next.Value.AimQ, next.Value.Held, next.Value.Pressed | first.Value.Pressed);
            }
            if (peer.Inputs.Count > 0)
            {
                var first = peer.Inputs.First();
                peer.Inputs.Remove(first.Key);
                peer.LastInput = first.Value;
                return first.Value;
            }
            var last = peer.LastInput;
            return new PlayerInput(last.Sequence, last.MoveX, last.MoveY, last.AimQ, last.Held, InputButtons.None);
        }

        private void PumpTransport()
        {
            while (this.transport.TryReceive(out var e))
            {
                switch (e.Type)
                {
                    case TransportEventType.Connected:
                        this.peers[e.Peer] = new Peer { Handle = e.Peer, Stage = Stage.AwaitingHello, LastPacketTick = this.sessionTick };
                        break;
                    case TransportEventType.Disconnected:
                        this.DropPeer(e.Peer, "connection lost", notify: false);
                        break;
                    case TransportEventType.Data:
                        if (!this.peers.TryGetValue(e.Peer, out var peer))
                            break;
                        peer.LastPacketTick = this.sessionTick;
                        try
                        {
                            this.Handle(peer, e.Data!);
                        }
                        catch (ProtocolException ex)
                        {
                            this.Log?.Invoke($"protocol violation from peer {e.Peer}: {ex.Message}");
                            this.DropPeer(e.Peer, "protocol violation", notify: true);
                        }
                        break;
                }
            }
        }

        private void CheckTimeouts()
        {
            int limit = SimTime.Ticks(this.TimeoutSeconds);
            foreach (var peer in this.peers.Values.ToList())
            {
                if (this.sessionTick - peer.LastPacketTick > limit)
                    this.DropPeer(peer.Handle, "timeout", notify: true);
            }
        }

        /// <summary>Same handling for intentional and accidental disconnects: the character stays in the world.</summary>
        private void DropPeer(long handle, string reason, bool notify)
        {
            if (!this.peers.Remove(handle, out var peer))
                return;
            if (notify)
                this.transport.Disconnect(handle);
            if (peer.ParticipantId.Length == 0)
                return;
            this.Log?.Invoke($"{peer.ParticipantId} disconnected ({reason})");
            var world = this.Controller?.World;
            var player = world?.FindPlayerByParticipant(peer.ParticipantId);
            if (player != null && !this.peers.Values.Any(p => p.ParticipantId == peer.ParticipantId))
                world!.SetConnected(player.Id, false);
            if (!this.Lobby.RunStarted)
            {
                this.Lobby.Members.RemoveAll(m => m.ParticipantId == peer.ParticipantId);
                this.BroadcastLobby();
            }
        }

        private void Handle(Peer peer, byte[] data)
        {
            var type = Protocol.PeekType(data);
            switch (peer.Stage)
            {
                case Stage.AwaitingHello when type == MessageType.Hello:
                    this.OnHello(peer, HelloMessage.Decode(data));
                    return;
                case Stage.AwaitingChallenge when type == MessageType.ChallengeResponse:
                    this.OnChallengeResponse(peer, Protocol.Open(data, MessageType.ChallengeResponse).Bytes(512));
                    return;
                case Stage.AwaitingFullStateAck when type == MessageType.FullStateAck:
                    this.OnFullStateAck(peer);
                    return;
                case Stage.AwaitingFullStateAck when type == MessageType.InputBatch:
                    return; // inputs before the client has the state are ignored
                case Stage.Active when type == MessageType.InputBatch:
                    this.OnInputs(peer, InputCodec.DecodeBatch(data).inputs);
                    return;
                case Stage.Active when type == MessageType.FullStateAck:
                    return;
                case Stage.Active or Stage.AwaitingFullStateAck when type == MessageType.ActionRequest:
                    this.OnAction(peer, ActionRequest.Decode(data));
                    return;
                case Stage.Active or Stage.AwaitingFullStateAck when type == MessageType.Ping:
                    this.transport.Send(peer.Handle, Protocol.Begin(MessageType.Pong).I32(Protocol.Open(data, MessageType.Ping).I32()).ToArray(), reliable: false);
                    return;
                default:
                    throw new ProtocolException($"{type} not allowed in stage {peer.Stage}");
            }
        }

        private void Reject(Peer peer, string reason)
        {
            this.transport.Send(peer.Handle, Protocol.Begin(MessageType.Reject).Str(reason).ToArray(), reliable: true);
            this.Log?.Invoke($"rejected peer {peer.Handle}: {reason}");
            this.peers.Remove(peer.Handle);
            this.transport.Disconnect(peer.Handle);
        }

        private void OnHello(Peer peer, HelloMessage hello)
        {
            if (hello.ProtocolVersion != Protocol.Version)
            {
                this.Reject(peer, $"Protokollversion {hello.ProtocolVersion} passt nicht zu {Protocol.Version}");
                return;
            }
            if (hello.GameVersion != this.GameVersion)
            {
                this.Reject(peer, $"Spielversion {hello.GameVersion} passt nicht zu {this.GameVersion}");
                return;
            }
            if (hello.ContentHash != this.data.ContentHash)
            {
                this.Reject(peer, "Spieldaten unterscheiden sich vom Host");
                return;
            }
            if (hello.AssetHash != this.assetHash)
            {
                this.Reject(peer, "Grafik-/Audiodateien unterscheiden sich vom Host");
                return;
            }
            peer.Hello = hello;
            peer.Nonce = RandomNumberGenerator.GetBytes(32);
            peer.Stage = Stage.AwaitingChallenge;
            this.transport.Send(peer.Handle, Protocol.Begin(MessageType.Challenge).Bytes(peer.Nonce).ToArray(), reliable: true);
        }

        private void OnChallengeResponse(Peer peer, byte[] signature)
        {
            var hello = peer.Hello!;
            if (!ParticipantIdentity.Verify(hello.PublicKey, peer.Nonce, signature))
            {
                this.Reject(peer, "Identitätsprüfung fehlgeschlagen");
                return;
            }
            string id = ParticipantIdentity.IdFromPublicKey(hello.PublicKey);
            if (id == this.HostParticipantId)
            {
                this.Reject(peer, "Identität des Hosts");
                return;
            }
            var run = this.Controller?.Run;
            bool reconnect = false;
            if (this.Lobby.RunStarted)
            {
                if (run == null || !run.OriginalParticipants.Contains(id))
                {
                    this.Reject(peer, "Der Run läuft bereits; neue Spieler können nicht beitreten");
                    return;
                }
                reconnect = true;
            }
            else if (!this.Lobby.Members.Any(m => m.ParticipantId == id) && this.Lobby.Members.Count >= Protocol.MaxPlayers)
            {
                this.Reject(peer, "Die Lobby ist voll");
                return;
            }

            // a verified newer connection replaces a stale one of the same participant
            foreach (var old in this.peers.Values.Where(p => p != peer && p.ParticipantId == id).ToList())
                this.DropPeer(old.Handle, "replaced by new connection", notify: true);
            peer.ParticipantId = id;
            this.transport.Send(peer.Handle, Protocol.Begin(MessageType.Welcome).Str(id).Bool(reconnect).ToArray(), reliable: true);

            if (!this.Lobby.RunStarted)
            {
                this.Lobby.Members.RemoveAll(m => m.ParticipantId == id);
                var cls = hello.UnlockedClasses.FirstOrDefault(c => this.data.Classes.ContainsKey(c)) ?? this.data.Classes.Keys.First();
                this.Lobby.Members.Add(new LobbyMember
                {
                    ParticipantId = id,
                    DisplayName = Sanitize(hello.DisplayName),
                    ClassId = cls,
                    WeaponTypeId = this.data.Classes[cls].WeaponTypes[0],
                    UnlockedClasses = hello.UnlockedClasses.Where(this.data.Classes.ContainsKey).ToList(),
                    UnlockedDifficulties = hello.UnlockedDifficulties.ToList(),
                });
                peer.Stage = Stage.Active;
                this.BroadcastLobby();
                return;
            }
            this.Log?.Invoke($"{id} reconnected");
            this.SendFullState(peer, reconnect: true);
        }

        private void SendFullState(Peer peer, bool reconnect)
        {
            var c = this.Controller!;
            var world = c.World?.CaptureState();
            var payload = new FullStatePayload
            {
                Phase = c.Phase,
                World = world,
                Run = c.Run,
                IsReconnect = reconnect,
                YourPlayerEntityId = world?.Players.Find(p => p.ParticipantId == peer.ParticipantId)?.Id ?? -1,
            };
            this.transport.Send(peer.Handle, Protocol.Begin(MessageType.FullState).Bytes(Protocol.CompressJson(payload)).ToArray(), reliable: true);
            peer.Stage = Stage.AwaitingFullStateAck;
            peer.PendingProtection = reconnect && world != null;
            peer.Inputs.Clear();
        }

        private void OnFullStateAck(Peer peer)
        {
            peer.Stage = Stage.Active;
            var world = this.Controller?.World;
            var player = world?.FindPlayerByParticipant(peer.ParticipantId);
            if (player != null)
            {
                world!.SetConnected(player.Id, true);
                if (peer.PendingProtection)
                    world.GrantReconnectProtection(player.Id); // 2 s: no damage, no actions; HP/lives/cooldowns unchanged
                peer.LastInput = new PlayerInput(player.LastProcessedInput, 0, 0, 0, InputButtons.None, InputButtons.None);
            }
            peer.PendingProtection = false;
        }

        private void OnInputs(Peer peer, List<PlayerInput> inputs)
        {
            var player = this.Controller?.World?.FindPlayerByParticipant(peer.ParticipantId);
            uint processed = player?.LastProcessedInput ?? 0;
            foreach (var i in inputs)
            {
                // duplicates and stale redundancy copies are ignored by sequence number
                if (i.Sequence > processed && i.Sequence > peer.LastInput.Sequence)
                    peer.Inputs[i.Sequence] = i;
            }
            while (peer.Inputs.Count > 32)
                peer.Inputs.Remove(peer.Inputs.First().Key);
        }

        private void OnAction(Peer peer, ActionRequest req)
        {
            if (peer.HandledRequests.TryGetValue(req.RequestId, out var cached))
            {
                this.SendActionResult(peer, req.RequestId, cached.ok, cached.reason);
                return;
            }
            var (ok, reason) = this.Execute(peer.ParticipantId, req);
            peer.HandledRequests[req.RequestId] = (ok, reason);
            this.SendActionResult(peer, req.RequestId, ok, reason);
        }

        private (bool ok, string reason) Execute(string participantId, ActionRequest req)
        {
            var c = this.Controller;
            switch (req.Action)
            {
                case ClientAction.SetClass:
                    return this.SetClass(participantId, req.A) ? (true, "") : (false, "Klasse nicht verfügbar");
                case ClientAction.SetWeapon:
                    return this.SetWeapon(participantId, req.A) ? (true, "") : (false, "Waffe nicht verfügbar");
                case ClientAction.SetReady:
                {
                    var m = this.Lobby.Members.Find(x => x.ParticipantId == participantId);
                    if (m == null || this.Lobby.RunStarted)
                        return (false, "nicht in der Lobby");
                    m.Ready = req.Flag;
                    this.BroadcastLobby();
                    return (true, "");
                }
                case ClientAction.PactVote:
                {
                    if (c == null)
                        return (false, "kein Run");
                    var r = c.VotePact(participantId, req.Flag);
                    this.BroadcastNotice("Pakt-Abstimmung aktualisiert");
                    return (r.Succeeded, r.Reason ?? "");
                }
                case ClientAction.SellItem:
                case ClientAction.TradeItem:
                case ClientAction.MoveToChest:
                case ClientAction.TakeFromChest:
                case ClientAction.ClaimPending:
                {
                    if (c == null || c.Phase != RunPhase.BetweenArenas)
                        return (false, "nur zwischen den Arenen möglich");
                    var kind = req.Action switch
                    {
                        ClientAction.SellItem => TxKind.SellItem,
                        ClientAction.TradeItem => TxKind.TradeItem,
                        ClientAction.MoveToChest => TxKind.MoveToChest,
                        ClientAction.TakeFromChest => TxKind.TakeFromChest,
                        _ => TxKind.ClaimPendingReward,
                    };
                    // the request id makes client retries idempotent even across host restarts
                    var r = c.Ledger.Commit(new RunTransaction { TxId = $"req:{c.Run.RunId}:{participantId}:{req.RequestId}", Kind = kind, ParticipantId = participantId, ItemId = req.A, OtherParticipantId = req.B.Length > 0 ? req.B : null });
                    if (r.Succeeded)
                        this.BroadcastNotice("");
                    return (r.Outcome != TxOutcome.Rejected, r.Reason ?? "");
                }
                default:
                    return (false, "unbekannte Aktion");
            }
        }

        private void SendActionResult(Peer peer, uint requestId, bool ok, string reason) =>
            this.transport.Send(peer.Handle, Protocol.Begin(MessageType.ActionResult).U32(requestId).Bool(ok).Str(reason).ToArray(), reliable: true);

        private void BroadcastLobby()
        {
            this.Lobby.StartBlockers = this.StartBlockers();
            byte[] msg = Protocol.Begin(MessageType.LobbyState).Bytes(Protocol.CompressJson(this.Lobby)).ToArray();
            foreach (var peer in this.peers.Values.Where(p => p.Stage == Stage.Active))
                this.transport.Send(peer.Handle, msg, reliable: true);
        }

        private void BroadcastNotice(string message)
        {
            var c = this.Controller;
            if (c == null)
                return;
            var payload = new RunNoticePayload { Phase = c.Phase, Run = c.Run, PendingPactVoters = c.PendingPactVoters().ToList(), Message = message };
            byte[] msg = Protocol.Begin(MessageType.RunNotice).Bytes(Protocol.CompressJson(payload)).ToArray();
            foreach (var peer in this.peers.Values.Where(p => p.Stage == Stage.Active))
                this.transport.Send(peer.Handle, msg, reliable: true);
        }

        /// <summary>Host-side pact vote of the host's own participant.</summary>
        public TxResult HostVotePact(bool accept)
        {
            var r = this.Controller?.VotePact(this.HostParticipantId, accept) ?? TxResult.Reject("no run");
            this.BroadcastNotice("Pakt-Abstimmung aktualisiert");
            return r;
        }
    }
}
