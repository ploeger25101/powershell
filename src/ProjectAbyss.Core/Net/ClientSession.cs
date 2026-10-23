using System;
using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Combat;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Net
{
    public enum ClientStage
    {
        Connecting,
        Authenticating,
        Lobby,
        InRun,
        Rejected,
        Disconnected,
    }

    /// <summary>
    /// Client side: sends inputs, keeps a replica of the host's world for rendering and predicts only its own
    /// movement (corrected on every snapshot). Never decides damage, loot or gold.
    /// </summary>
    public sealed class ClientSession
    {
        private const int MaxUnacked = 180;
        private readonly INetTransport transport;
        private readonly GameData data;
        private readonly ParticipantIdentity identity;
        private readonly HelloMessage hello;
        private readonly SnapshotCodec codec;
        private readonly AttackRegistry attacks;
        private readonly List<PlayerInput> unacked = new();
        private long hostHandle = -1;
        private uint inputSequence;
        private InputButtons lastHeld;
        private uint nextRequestId = 1;

        public ClientSession(INetTransport transport, GameData data, ParticipantIdentity identity, string displayName, IEnumerable<string> unlockedClasses, IEnumerable<string> unlockedDifficulties, string assetHash)
        {
            this.transport = transport;
            this.data = data;
            this.identity = identity;
            this.codec = new SnapshotCodec(new ContentIndex(data));
            this.attacks = new AttackRegistry(data);
            this.hello = new HelloMessage
            {
                GameVersion = EnvelopeIO.GameVersion,
                ContentHash = data.ContentHash,
                AssetHash = assetHash,
                PublicKey = identity.PublicKey,
                DisplayName = displayName,
                UnlockedClasses = unlockedClasses.ToList(),
                UnlockedDifficulties = unlockedDifficulties.ToList(),
            };
        }

        public ClientStage Stage { get; private set; } = ClientStage.Connecting;
        public string? RejectReason { get; private set; }
        public bool WasReconnect { get; private set; }
        public LobbyView? Lobby { get; private set; }
        public RunState? Run { get; private set; }
        public RunPhase Phase { get; private set; }
        public List<string> PendingPactVoters { get; private set; } = new();
        public WorldState? World { get; private set; }
        public int PlayerEntityId { get; private set; } = -1;
        public int LastSnapshotTick { get; private set; } = -1;
        public Vec2 PredictedPosition => this.prediction.Position;
        private Prediction prediction;

        /// <summary>Locally predicted movement state of the own character.</summary>
        private struct Prediction
        {
            public Vec2 Position;
            public int DashCharges;
            public int DashTicksLeft;
            public Vec2 DashDirection;
        }

        public List<float> Corrections { get; } = new();
        public float LastCorrectionDistance { get; private set; }
        public Dictionary<uint, (bool ok, string reason)> ActionResults { get; } = new();
        public List<SimEvent> ReceivedEvents { get; } = new();
        public string GameVersion { get => this.hello.GameVersion; set => this.hello.GameVersion = value; }

        public event Action<string>? Log;

        public PlayerEntity? OwnPlayer => this.World?.Players.Find(p => p.Id == this.PlayerEntityId);

        public void Tick(Vec2 move, float aim, InputButtons held)
        {
            this.Pump();
            if (this.Stage != ClientStage.InRun || this.World == null || this.hostHandle < 0)
                return;
            var pressed = held & ~this.lastHeld;
            this.lastHeld = held;
            var input = PlayerInput.Create(++this.inputSequence, move, aim, held, pressed);
            this.unacked.Add(input);
            if (this.unacked.Count > MaxUnacked)
                this.unacked.RemoveAt(0);
            this.Predict(input);
            // send the newest inputs with redundancy so a lost packet does not lose button presses
            var batch = this.unacked.Skip(Math.Max(0, this.unacked.Count - Protocol.InputRedundancy)).ToList();
            this.transport.Send(this.hostHandle, InputCodec.EncodeBatch(this.LastSnapshotTick, batch), reliable: false);
        }

        public void Pump()
        {
            while (this.transport.TryReceive(out var e))
            {
                switch (e.Type)
                {
                    case TransportEventType.Connected:
                        this.hostHandle = e.Peer;
                        this.Stage = ClientStage.Authenticating;
                        this.transport.Send(e.Peer, this.hello.Encode(), reliable: true);
                        break;
                    case TransportEventType.Disconnected:
                        if (this.Stage != ClientStage.Rejected)
                            this.Stage = ClientStage.Disconnected;
                        this.hostHandle = -1;
                        break;
                    case TransportEventType.Data:
                        try
                        {
                            this.Handle(e.Data!);
                        }
                        catch (ProtocolException ex)
                        {
                            this.Log?.Invoke("invalid data from host: " + ex.Message);
                            this.transport.Disconnect(e.Peer);
                            this.Stage = ClientStage.Disconnected;
                        }
                        break;
                }
            }
        }

        public uint SendAction(ClientAction action, string a = "", string b = "", long amount = 0, bool flag = false)
        {
            uint id = this.nextRequestId++;
            this.Resend(new ActionRequest { RequestId = id, Action = action, A = a, B = b, Amount = amount, Flag = flag });
            return id;
        }

        /// <summary>Re-sending the same request id is safe: the host answers from its cache instead of executing again.</summary>
        public void Resend(ActionRequest req)
        {
            if (this.hostHandle >= 0)
                this.transport.Send(this.hostHandle, req.Encode(), reliable: true);
        }

        private void Handle(byte[] data)
        {
            switch (Protocol.PeekType(data))
            {
                case MessageType.Challenge:
                {
                    byte[] nonce = Protocol.Open(data, MessageType.Challenge).Bytes(64);
                    this.transport.Send(this.hostHandle, Protocol.Begin(MessageType.ChallengeResponse).Bytes(this.identity.Sign(nonce)).ToArray(), reliable: true);
                    break;
                }
                case MessageType.Welcome:
                {
                    var r = Protocol.Open(data, MessageType.Welcome);
                    string id = r.Str(64);
                    this.WasReconnect = r.Bool();
                    if (id != this.identity.ParticipantId)
                        throw new ProtocolException("host assigned a different identity");
                    this.Stage = this.WasReconnect ? ClientStage.InRun : ClientStage.Lobby;
                    break;
                }
                case MessageType.Reject:
                    this.RejectReason = Protocol.Open(data, MessageType.Reject).Str(512);
                    this.Stage = ClientStage.Rejected;
                    break;
                case MessageType.LobbyState:
                    this.Lobby = Protocol.DecompressJson<LobbyView>(Protocol.Open(data, MessageType.LobbyState).Bytes());
                    break;
                case MessageType.FullState:
                    this.OnFullState(Protocol.DecompressJson<FullStatePayload>(Protocol.Open(data, MessageType.FullState).Bytes()));
                    break;
                case MessageType.Snapshot:
                    this.OnSnapshot(data);
                    break;
                case MessageType.RunNotice:
                {
                    var notice = Protocol.DecompressJson<RunNoticePayload>(Protocol.Open(data, MessageType.RunNotice).Bytes());
                    this.Run = notice.Run;
                    this.Phase = notice.Phase;
                    this.PendingPactVoters = notice.PendingPactVoters;
                    if (notice.Phase != RunPhase.InArena)
                        this.World = null;
                    break;
                }
                case MessageType.ActionResult:
                {
                    var r = Protocol.Open(data, MessageType.ActionResult);
                    uint id = r.U32();
                    this.ActionResults[id] = (r.Bool(), r.Str(512));
                    break;
                }
                case MessageType.Pong:
                    break;
                default:
                    throw new ProtocolException("unexpected message from host");
            }
        }

        private void OnFullState(FullStatePayload payload)
        {
            this.Stage = ClientStage.InRun;
            this.Phase = payload.Phase;
            this.Run = payload.Run;
            this.World = payload.World;
            this.PlayerEntityId = payload.YourPlayerEntityId;
            this.unacked.Clear();
            this.LastSnapshotTick = this.World?.Tick ?? -1;
            var own = this.OwnPlayer;
            if (own != null)
            {
                // continue the input sequence where the host left off (the client may have restarted)
                this.inputSequence = Math.Max(this.inputSequence, own.LastProcessedInput);
                this.prediction = FromAuthoritative(own, this.World!.Tick);
            }
            this.transport.Send(this.hostHandle, Protocol.Begin(MessageType.FullStateAck).I32(this.LastSnapshotTick).ToArray(), reliable: true);
        }

        private void OnSnapshot(byte[] data)
        {
            if (this.World == null)
                return;
            var r = new NetReader(data);
            r.U8();
            int tick = r.I32();
            if (tick <= this.LastSnapshotTick)
                return; // late or duplicated unreliable packet
            var decoded = this.codec.Apply(data, this.World);
            this.LastSnapshotTick = decoded.Tick;
            this.ReceivedEvents.AddRange(decoded.Events);
            if (this.ReceivedEvents.Count > 2048)
                this.ReceivedEvents.RemoveRange(0, this.ReceivedEvents.Count - 2048);
            this.unacked.RemoveAll(i => i.Sequence <= decoded.AckInputSeq);
            var own = this.OwnPlayer;
            if (own == null)
                return;
            // reconciliation: start from the authoritative position and replay inputs the host has not processed yet
            var before = this.prediction.Position;
            var p = FromAuthoritative(own, this.World.Tick);
            int t = this.World.Tick;
            foreach (var input in this.unacked)
                p = this.PredictStep(own, p, input, t++);
            this.prediction = p;
            this.LastCorrectionDistance = Vec2.Distance(before, p.Position);
            this.Corrections.Add(this.LastCorrectionDistance);
        }

        private static Prediction FromAuthoritative(PlayerEntity own, int tick) => new()
        {
            Position = own.Position,
            DashCharges = own.DashCharges,
            DashTicksLeft = Math.Max(0, own.DashEndTick - tick),
            DashDirection = own.DashDirection,
        };

        private void Predict(PlayerInput input)
        {
            var own = this.OwnPlayer;
            if (own != null && this.World != null)
                this.prediction = this.PredictStep(own, this.prediction, input, this.World.Tick + this.unacked.Count);
        }

        /// <summary>Movement-only prediction mirroring the host's movement rules (dash start/movement, attack slow, stun).</summary>
        private Prediction PredictStep(PlayerEntity own, Prediction p, PlayerInput input, int tick)
        {
            if (own.LifeState != LifeState.Alive || own.Statuses.Any(s => s.Type == StatusType.Stun && s.ExpireTick > tick))
                return p;
            var arena = this.World!.Arena;
            var dash = this.data.Balance.Dash;
            if ((input.Pressed & InputButtons.Dash) != 0 && p.DashTicksLeft <= 0 && p.DashCharges > 0
                && (own.Action == null || own.Action.PhaseAt(tick) is ActionPhase.Recovery or ActionPhase.Done))
            {
                p.DashCharges--;
                p.DashTicksLeft = SimTime.Ticks(dash.DurationSeconds);
                var m = input.Move;
                p.DashDirection = m.LengthSquared > 0.01f ? m.Normalized() : Vec2.FromAngle(input.AimRadians);
            }
            if (p.DashTicksLeft > 0)
            {
                p.DashTicksLeft--;
                p.Position = arena.MoveCircle(p.Position, p.DashDirection * (dash.Distance / Math.Max(SimTime.TickSeconds, dash.DurationSeconds)) * SimTime.TickSeconds, own.Radius);
                return p;
            }
            float speed = own.MoveSpeed;
            foreach (var b in own.Buffs)
                speed *= this.data.Abilities.Values.FirstOrDefault(a => a.SelfBuff?.Id == b.BuffId)?.SelfBuff?.MoveSpeedMultiplier ?? 1f;
            if (own.Action != null && own.Action.PhaseAt(tick) is ActionPhase.Windup or ActionPhase.Active
                && this.attacks.TryGet(own.Action.AttackKey, out var atk))
                speed *= atk.MoveSpeedMultiplier;
            p.Position = arena.MoveCircle(p.Position, input.Move * speed * SimTime.TickSeconds, own.Radius);
            return p;
        }
    }
}
