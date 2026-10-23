using System.Diagnostics;
using System.Net;
using System.Threading;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Net;
using ProjectAbyss.Core.Simulation;
using Xunit;

namespace ProjectAbyss.Core.Tests
{
    public class TcpTests
    {
        [Fact]
        public void HostAndClientPlayOverRealLoopbackSockets()
        {
            using var hostTransport = TcpTransport.Listen(IPAddress.Loopback, 0);
            using var hostId = ParticipantIdentity.Create();
            using var clientId = ParticipantIdentity.Create();
            var host = new HostSession(hostTransport, TestData.Data, hostId.ParticipantId, "Host", new[] { "warrior" }, new[] { "normal" }, "a", _ => null);
            using var clientTransport = TcpTransport.Connect("127.0.0.1", hostTransport.ListenPort);
            var client = new ClientSession(clientTransport, TestData.Data, clientId, "Gast", new[] { "warrior" }, new[] { "normal" }, "a");

            bool Pump(System.Func<bool> until, int maxMs = 5000)
            {
                var sw = Stopwatch.StartNew();
                while (!until() && sw.ElapsedMilliseconds < maxMs)
                {
                    host.Tick();
                    client.Tick(new Vec2(1, 0), 0, InputButtons.None);
                    Thread.Sleep(2);
                }
                return until();
            }

            Assert.True(Pump(() => client.Stage == ClientStage.Lobby), $"stage {client.Stage} {client.RejectReason}");
            client.SendAction(ClientAction.SetReady, flag: true);
            host.SetHostReady(true);
            Assert.True(Pump(() => host.StartBlockers().Count == 0));
            host.StartRun(5, developer: false);
            Assert.True(Pump(() => client.Stage == ClientStage.InRun && client.LastSnapshotTick > 30));
            var serverPlayer = host.Controller!.World!.FindPlayer(client.PlayerEntityId)!;
            Assert.True(serverPlayer.Position.X > host.Controller.World.State.Arena.PlayerStarts[1].Center.X, "client input moved the host-side character");
            clientTransport.Dispose();
            Assert.True(Pump(() => !serverPlayer.Connected));
        }
    }
}
