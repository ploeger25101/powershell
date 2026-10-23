using System.Collections.Generic;
using System.Linq;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Net;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace ProjectAbyss.Core.Tests
{
    /// <summary>Host + clients over the in-memory network with a virtual clock.</summary>
    internal sealed class NetHarness
    {
        public readonly InMemoryNetwork Net = new(7);
        public readonly InMemoryNetwork.Endpoint HostEndpoint;
        public readonly ParticipantIdentity HostIdentity = ParticipantIdentity.Create();
        public HostSession Host;
        public readonly List<(ClientSession Session, InMemoryNetwork.Endpoint Endpoint, ParticipantIdentity Id, AutoPilot Bot)> Clients = new();
        public AutoPilot HostBot = new(99);
        public string? StoreDir;

        public NetHarness(string? storeDir = null)
        {
            this.StoreDir = storeDir;
            this.HostEndpoint = this.Net.CreateEndpoint();
            this.Host = this.CreateHost();
        }

        public HostSession CreateHost() => new(this.HostEndpoint, TestData.Data, this.HostIdentity.ParticipantId, "Host", new[] { "warrior" }, new[] { "normal" }, "assets-1",
            run => this.StoreDir == null ? null : new RunStore(System.IO.Path.Combine(this.StoreDir, run.RunId)));

        public ClientSession AddClient(string name, ParticipantIdentity? id = null, string contentHashOverride = "", string asset = "assets-1")
        {
            id ??= ParticipantIdentity.Create();
            var ep = this.Net.CreateEndpoint();
            var client = new ClientSession(ep, TestData.Data, id, name, new[] { "warrior" }, new[] { "normal" }, asset);
            this.Clients.Add((client, ep, id, new AutoPilot((ulong)this.Clients.Count + 1)));
            this.Net.Connect(this.HostEndpoint, ep);
            return client;
        }

        public void Tick(int n = 1, bool bots = true)
        {
            for (int i = 0; i < n; i++)
            {
                this.Net.Advance();
                var hw = this.Host.Controller?.World;
                if (bots && hw != null)
                {
                    var hp = hw.FindPlayerByParticipant(this.HostIdentity.ParticipantId);
                    if (hp != null)
                        this.Host.SetHostInput(this.HostBot.Next(hw.State, hp.Id));
                }
                this.Host.Tick();
                foreach (var c in this.Clients)
                {
                    if (bots && c.Session.World != null && c.Session.PlayerEntityId >= 0)
                    {
                        var input = c.Bot.Next(c.Session.World, c.Session.PlayerEntityId);
                        c.Session.Tick(input.Move, input.AimRadians, input.Held);
                    }
                    else
                    {
                        c.Session.Tick(Vec2.Zero, 0, InputButtons.None);
                    }
                }
            }
        }

        public void StartRun()
        {
            this.Tick(10, bots: false);
            foreach (var c in this.Clients)
                c.Session.SendAction(ClientAction.SetReady, flag: true);
            this.Host.SetHostReady(true);
            this.Tick(10, bots: false);
            this.Host.StartRun(1234, developer: false);
            this.Tick(10, bots: false);
        }
    }

    public class NetworkTests
    {
        private readonly ITestOutputHelper output;

        public NetworkTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void ClientJoinsLobbyAndEntersRun()
        {
            var h = new NetHarness();
            var c = h.AddClient("Zweiter");
            h.Tick(10, bots: false);
            Assert.Equal(ClientStage.Lobby, c.Stage);
            Assert.Equal(2, h.Host.Lobby.Members.Count);
            Assert.Contains(h.Host.StartBlockers(), b => b.Contains("nicht bereit"));
            h.StartRun();
            Assert.Equal(ClientStage.InRun, c.Stage);
            Assert.NotNull(c.World);
            Assert.Equal(2, c.World!.Players.Count);
            Assert.True(c.PlayerEntityId > 0);
        }

        [Fact]
        public void MismatchedVersionsAndContentAreRejected()
        {
            var h = new NetHarness();
            var wrongAssets = h.AddClient("A", asset: "other-assets");
            var oldVersion = h.AddClient("B");
            oldVersion.GameVersion = "0.0.1-old";
            h.Tick(10, bots: false);
            Assert.Equal(ClientStage.Rejected, wrongAssets.Stage);
            Assert.Contains("Grafik", wrongAssets.RejectReason);
            Assert.Equal(ClientStage.Rejected, oldVersion.Stage);
            Assert.Single(h.Host.Lobby.Members);
        }

        [Fact]
        public void LobbyIsLimitedToFourAndNewPlayersCannotJoinStartedRun()
        {
            var h = new NetHarness();
            for (int i = 0; i < 3; i++)
                h.AddClient("C" + i);
            h.Tick(10, bots: false);
            var fifth = h.AddClient("Fünf");
            h.Tick(10, bots: false);
            Assert.Equal(ClientStage.Rejected, fifth.Stage);
            h.Clients.RemoveAt(h.Clients.Count - 1);
            h.StartRun();
            var late = h.AddClient("Spät");
            h.Tick(10, bots: false);
            Assert.Equal(ClientStage.Rejected, late.Stage);
            Assert.Contains("Run läuft bereits", late.RejectReason);
        }

        [Fact]
        public void CoopArenaOverLossyNetworkIsClearedAndReplicaConverges()
        {
            var h = new NetHarness();
            var c = h.AddClient("Zweiter");
            h.StartRun();
            h.Net.Conditions.LatencyTicks = 4;
            h.Net.Conditions.JitterTicks = 3;
            h.Net.Conditions.Loss = 0.1f;
            h.Net.Conditions.Duplicate = 0.05f;
            float maxCorrection = 0;
            int ticks = 0;
            while (h.Host.Controller!.Phase == RunPhase.InArena && ticks++ < SimTime.Ticks(240f))
            {
                h.Tick();
                if (c.LastCorrectionDistance > maxCorrection)
                    maxCorrection = c.LastCorrectionDistance;
            }
            var corr = c.Corrections.OrderBy(x => x).ToList();
            this.output.WriteLine($"corrections: n={corr.Count} median={corr[corr.Count / 2]:0.00}px p95={corr[(int)(corr.Count * 0.95)]:0.00}px");
            this.output.WriteLine($"cleared after {SimTime.Seconds(ticks):0.0}s, dropped {h.Net.DroppedPackets}, duplicated {h.Net.DuplicatedPackets}, max correction {maxCorrection:0.00}px");
            Assert.Equal(RunPhase.BetweenArenas, h.Host.Controller.Phase);
            Assert.True(h.Net.DroppedPackets > 0);
            h.Tick(20, bots: false);
            Assert.Equal(RunPhase.BetweenArenas, c.Phase);
            Assert.Equal(h.Host.Controller.Run.Get(c.OwnId()).Gold, c.Run!.Get(c.OwnId()).Gold);
        }

        [Fact]
        public void PredictionMatchesHostWhenInputStops()
        {
            var h = new NetHarness();
            var c = h.AddClient("Zweiter");
            h.StartRun();
            h.Net.Conditions.LatencyTicks = 6;
            for (int i = 0; i < 60; i++)
            {
                h.Net.Advance();
                h.Host.Tick();
                c.Tick(new Vec2(0, 1), 0, InputButtons.None);
            }
            for (int i = 0; i < 30; i++)
            {
                h.Net.Advance();
                h.Host.Tick();
                c.Tick(Vec2.Zero, 0, InputButtons.None);
            }
            var hostPos = h.Host.Controller!.World!.FindPlayer(c.PlayerEntityId)!.Position;
            Assert.True(Vec2.Distance(hostPos, c.PredictedPosition) < 0.5f, $"host {hostPos} predicted {c.PredictedPosition}");
            Assert.True(hostPos.Y > h.Host.Controller.World.State.Arena.PlayerStarts[1].Center.Y + 20);
        }

        [Fact]
        public void ReconnectMidCombatRestoresStateWithProtectionAndNoFreeHealing()
        {
            var h = new NetHarness();
            var id = ParticipantIdentity.Create();
            var c = h.AddClient("Zweiter", id);
            h.StartRun();
            h.Tick(SimTime.Ticks(4f));
            var world = h.Host.Controller!.World!;
            var player = world.FindPlayer(c.PlayerEntityId)!;
            player.Hp = player.MaxHp * 0.5f;
            player.CooldownReadyTick["warrior_whirlwind"] = world.Tick + 600;

            h.Net.Cut(h.HostEndpoint, h.Clients[0].Endpoint);
            h.Tick(5);
            Assert.False(player.Connected);
            float hpWhileGone = player.Hp;
            int livesWhileGone = player.Lives;

            // same identity, new connection (e.g. after a client crash)
            h.Clients.Clear();
            var c2 = h.AddClient("Zweiter", id);
            for (int i = 0; i < 30 && !(c2.Stage == ClientStage.InRun && player.Connected); i++)
                h.Tick(1, bots: false);
            Assert.True(c2.WasReconnect);
            Assert.True(player.Connected);
            Assert.True(player.IsProtected(world.Tick));
            Assert.True(player.Hp <= hpWhileGone);
            Assert.Equal(livesWhileGone, player.Lives);
            Assert.False(h.Host.Controller.World!.IsReady(player, "warrior_whirlwind"));
            Assert.Equal(c.PlayerEntityId, c2.PlayerEntityId);
            h.Tick(SimTime.Ticks(2.1f), bots: false);
            Assert.False(player.IsProtected(world.Tick));
        }

        [Fact]
        public void ImpostorCannotTakeOverASlot()
        {
            var h = new NetHarness();
            var c = h.AddClient("Zweiter");
            h.StartRun();
            var impostor = h.AddClient("Zweiter"); // same name, different key
            h.Tick(10, bots: false);
            Assert.Equal(ClientStage.Rejected, impostor.Stage);
        }

        [Fact]
        public void SilentDisconnectIsDetectedByTimeout()
        {
            var h = new NetHarness();
            var c = h.AddClient("Zweiter");
            h.StartRun();
            var player = h.Host.Controller!.World!.FindPlayer(c.PlayerEntityId)!;
            h.Net.Blackhole(h.HostEndpoint, h.Clients[0].Endpoint);
            h.Tick(SimTime.Ticks(TestData.Data.Balance.Reconnect.TimeoutSeconds) + 5, bots: false);
            Assert.False(player.Connected);
        }

        [Fact]
        public void MalformedPacketsDropOnlyTheSender()
        {
            var h = new NetHarness();
            var good = h.AddClient("Gut");
            h.Tick(10, bots: false);
            var evil = h.Net.CreateEndpoint();
            h.Net.Connect(h.HostEndpoint, evil);
            h.Net.Advance();
            evil.Send(h.HostEndpoint.Address, new byte[] { (byte)MessageType.Hello, 1 }, reliable: true);
            h.Tick(10, bots: false);
            Assert.Equal(ClientStage.Lobby, good.Stage);
            Assert.Equal(2, h.Host.Lobby.Members.Count);
        }

        [Fact]
        public void HostCrashAndRestartResumesRunAndClientRejoins()
        {
            using var dir = new TempDir();
            var h = new NetHarness(dir.Path);
            var id = ParticipantIdentity.Create();
            var c = h.AddClient("Zweiter", id);
            h.StartRun();
            h.Tick(SimTime.Ticks(5f));
            var runId = h.Host.Controller!.Run.RunId;
            long goldBefore = h.Host.Controller.Run.Participants.Sum(p => p.Gold);

            // host process dies: all links drop, sessions are gone
            h.Net.Cut(h.HostEndpoint, h.Clients[0].Endpoint);
            var recovered = new RunStore(System.IO.Path.Combine(dir.Path, runId)).Load(new TransactionProcessor(TestData.Data))!;
            Assert.NotNull(recovered.World);
            Assert.Equal(goldBefore, recovered.Run.Participants.Sum(p => p.Gold));
            h.Host = h.CreateHost();
            h.Host.ResumeRun(recovered);

            h.Clients.Clear();
            var c2 = h.AddClient("Zweiter", id);
            h.Tick(20, bots: false);
            Assert.Equal(ClientStage.InRun, c2.Stage);
            Assert.True(c2.WasReconnect);
            var p = h.Host.Controller!.World!.FindPlayerByParticipant(id.ParticipantId)!;
            Assert.True(p.Connected);
            int guard = 0;
            while (h.Host.Controller.Phase == RunPhase.InArena && guard++ < SimTime.Ticks(240f))
                h.Tick();
            Assert.Equal(RunPhase.BetweenArenas, h.Host.Controller.Phase);
        }

        [Fact]
        public void RepeatedActionRequestIsExecutedOnce()
        {
            var h = new NetHarness();
            var c = h.AddClient("Zweiter");
            h.StartRun();
            int guard = 0;
            while (h.Host.Controller!.Phase == RunPhase.InArena && guard++ < SimTime.Ticks(240f))
                h.Tick();
            var run = h.Host.Controller.Run;
            var me = run.Get(c.OwnId());
            var item = new Items.ItemInstance { InstanceId = run.NewItemId(), DefId = "leather_cap" };
            h.Host.Controller.Ledger.Commit(new RunTransaction { TxId = "test-grant", Kind = TxKind.GrantItem, ParticipantId = me.ParticipantId, Item = item });
            long gold = me.Gold;
            var req = new ActionRequest { RequestId = 77, Action = ClientAction.SellItem, A = item.InstanceId };
            c.Resend(req);
            c.Resend(req);
            h.Tick(10, bots: false);
            c.Resend(req);
            h.Tick(10, bots: false);
            Assert.True(c.ActionResults[77].ok);
            Assert.Equal(gold + new TransactionProcessor(TestData.Data).SellPrice(item), me.Gold);
        }
    }

    internal static class ClientTestExtensions
    {
        public static string OwnId(this ClientSession c) => c.World?.Players.Find(p => p.Id == c.PlayerEntityId)?.ParticipantId
            ?? c.Run!.Participants.Select(p => p.ParticipantId).First(id => id.StartsWith("pa-", System.StringComparison.Ordinal) && c.Run.Participants.Count > 0 && id != c.Run.Participants[0].ParticipantId);
    }
}
