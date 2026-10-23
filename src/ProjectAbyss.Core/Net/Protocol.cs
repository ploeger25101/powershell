using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Core.Net
{
    public enum MessageType : byte
    {
        // client → host
        Hello = 1,
        ChallengeResponse = 2,
        InputBatch = 3,
        FullStateAck = 4,
        ActionRequest = 5,
        Ping = 6,

        // host → client
        Challenge = 20,
        Welcome = 21,
        Reject = 22,
        Snapshot = 23,
        FullState = 24,
        ActionResult = 25,
        LobbyState = 26,
        RunNotice = 27,
        Pong = 28,
    }

    public static class Protocol
    {
        /// <summary>Bump on any wire-format change. Peers with a different version are rejected in the handshake.</summary>
        public const ushort Version = 1;
        public const int MaxPlayers = 4;
        public const int InputRedundancy = 4;
        public const int SnapshotIntervalTicks = 3;
        public const int MaxNameLength = 24;

        public static MessageType PeekType(byte[] data)
        {
            if (data.Length == 0)
                throw new ProtocolException("empty message");
            return (MessageType)data[0];
        }

        public static NetWriter Begin(MessageType type) => new NetWriter().U8((byte)type);

        public static NetReader Open(byte[] data, MessageType expected)
        {
            var r = new NetReader(data);
            if ((MessageType)r.U8() != expected)
                throw new ProtocolException("unexpected message type");
            return r;
        }

        public static byte[] CompressJson<T>(T value)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                JsonSerializer.Serialize(gz, value, GameData.JsonOptions);
            return ms.ToArray();
        }

        public static T DecompressJson<T>(byte[] data)
        {
            using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            // cap decompressed size to defend against decompression bombs
            using var limited = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
            {
                limited.Write(buffer, 0, read);
                if (limited.Length > 32 * 1024 * 1024)
                    throw new ProtocolException("state too large");
            }
            limited.Position = 0;
            return JsonSerializer.Deserialize<T>(limited, GameData.JsonOptions) ?? throw new ProtocolException("empty state");
        }
    }

    public sealed class HelloMessage
    {
        public ushort ProtocolVersion { get; set; } = Protocol.Version;
        public string GameVersion { get; set; } = "";
        public string ContentHash { get; set; } = "";
        public string AssetHash { get; set; } = "";
        public byte[] PublicKey { get; set; } = Array.Empty<byte>();
        public string DisplayName { get; set; } = "";
        public List<string> UnlockedClasses { get; set; } = new();
        public List<string> UnlockedDifficulties { get; set; } = new();

        public byte[] Encode()
        {
            var w = Protocol.Begin(MessageType.Hello).U16(this.ProtocolVersion).Str(this.GameVersion).Str(this.ContentHash).Str(this.AssetHash).Bytes(this.PublicKey).Str(this.DisplayName);
            w.U16((ushort)this.UnlockedClasses.Count);
            foreach (var c in this.UnlockedClasses)
                w.Str(c);
            w.U16((ushort)this.UnlockedDifficulties.Count);
            foreach (var d in this.UnlockedDifficulties)
                w.Str(d);
            return w.ToArray();
        }

        public static HelloMessage Decode(byte[] data)
        {
            var r = Protocol.Open(data, MessageType.Hello);
            var m = new HelloMessage
            {
                ProtocolVersion = r.U16(),
                GameVersion = r.Str(),
                ContentHash = r.Str(),
                AssetHash = r.Str(),
                PublicKey = r.Bytes(1024),
                DisplayName = r.Str(Protocol.MaxNameLength),
            };
            int n = r.Count(64);
            for (int i = 0; i < n; i++)
                m.UnlockedClasses.Add(r.Str(64));
            n = r.Count(16);
            for (int i = 0; i < n; i++)
                m.UnlockedDifficulties.Add(r.Str(64));
            return m;
        }
    }

    public enum ClientAction : byte
    {
        SetClass = 1,
        SetReady = 2,
        SetDifficulty = 3,
        PactVote = 4,
        SellItem = 5,
        TradeItem = 6,
        MoveToChest = 7,
        TakeFromChest = 8,
        ClaimPending = 9,
        ContributeRevive = 10,
        StartRun = 11,
        ContinueToNextArena = 12,
    }

    /// <summary>A request the host validates; <see cref="RequestId"/> makes retries idempotent per participant.</summary>
    public sealed class ActionRequest
    {
        public uint RequestId { get; set; }
        public ClientAction Action { get; set; }
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public long Amount { get; set; }
        public bool Flag { get; set; }

        public byte[] Encode() => Protocol.Begin(MessageType.ActionRequest).U32(this.RequestId).U8((byte)this.Action).Str(this.A).Str(this.B).I64(this.Amount).Bool(this.Flag).ToArray();

        public static ActionRequest Decode(byte[] data)
        {
            var r = Protocol.Open(data, MessageType.ActionRequest);
            return new ActionRequest { RequestId = r.U32(), Action = (ClientAction)r.U8(), A = r.Str(), B = r.Str(), Amount = r.I64(), Flag = r.Bool() };
        }
    }

    public static class InputCodec
    {
        public static byte[] EncodeBatch(int lastSnapshotTick, IReadOnlyList<PlayerInput> inputs)
        {
            var w = Protocol.Begin(MessageType.InputBatch).I32(lastSnapshotTick).U16((ushort)inputs.Count);
            foreach (var i in inputs)
                w.U32(i.Sequence).U8((byte)i.MoveX).U8((byte)i.MoveY).U16(i.AimQ).U16((ushort)i.Held).U16((ushort)i.Pressed);
            return w.ToArray();
        }

        public static (int lastSnapshotTick, List<PlayerInput> inputs) DecodeBatch(byte[] data)
        {
            var r = Protocol.Open(data, MessageType.InputBatch);
            int tick = r.I32();
            int n = r.Count(32);
            var list = new List<PlayerInput>(n);
            const ushort validButtons = 0xFF;
            for (int i = 0; i < n; i++)
            {
                uint seq = r.U32();
                sbyte mx = (sbyte)r.U8();
                sbyte my = (sbyte)r.U8();
                ushort aim = r.U16();
                ushort held = r.U16();
                ushort pressed = r.U16();
                if ((held & ~validButtons) != 0 || (pressed & ~validButtons) != 0)
                    throw new ProtocolException("invalid buttons");
                list.Add(new PlayerInput(seq, mx, my, aim, (InputButtons)held, (InputButtons)pressed));
            }
            return (tick, list);
        }
    }
}
