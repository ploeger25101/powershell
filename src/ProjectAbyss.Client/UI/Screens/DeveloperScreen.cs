using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Client.Session;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Net;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Client.UI.Screens
{
    /// <summary>
    /// Developer mode (design §33). Uses a separate developer profile and run folder; developer runs never create
    /// achievements or unlocks. Simulated co-op players are real <see cref="ClientSession"/>s over an in-memory network.
    /// </summary>
    public sealed class DeveloperScreen : Screen
    {
        private int level = 1;
        private int bots;
        private int difficulty;
        private ulong seed = 1;
        private int latency;

        public DeveloperScreen(AbyssApp app) : base(app)
        {
        }

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            var diffs = this.App.Data.Balance.Scaling.Difficulties;
            this.Ui.Title("Entwicklermodus", this.S(80), s);
            int i = 0;
            int top = 150;
            if (this.Ui.Button(this.Row(i++, top, 640), $"Ebene: {this.level}{(RunPlan.IsBossLevel(this.level) ? " (Boss)" : "")}"))
                this.level = this.level % RunPlan.TotalLevels + 1;
            if (this.Ui.Button(this.Row(i++, top, 640), $"Simulierte Mitspieler (Bots): {this.bots}"))
                this.bots = (this.bots + 1) % 4;
            if (this.Ui.Button(this.Row(i++, top, 640), $"Schwierigkeit: {diffs[this.difficulty].Name}"))
                this.difficulty = (this.difficulty + 1) % diffs.Count;
            if (this.Ui.Button(this.Row(i++, top, 640), $"Seed: {this.seed}"))
                this.seed = this.seed * 7 + 3;
            if (this.Ui.Button(this.Row(i++, top, 640), $"Simulierte Latenz: {this.latency * 1000 / 60} ms"))
                this.latency = (this.latency + 3) % 15;
            if (this.Ui.Button(this.Row(i++, top, 640), "Arena starten"))
                this.App.Show(new GameScreen(this.App, DevGameSession.Start(this.App, this.level, this.bots, diffs[this.difficulty].Id, this.seed, this.latency)));
            this.Ui.Paragraph("Tasten im Entwicklerkampf: F5 volle HP, F6 alle Gegner besiegen, F7 Reconnect eines Bots simulieren, F8 Host-Absturz simulieren (Wiederherstellung aus dem Speicher), F9 Gegnerwelle beschwören. Leistungswerte werden im Kampf eingeblendet.",
                new Rectangle(s.X / 2 - this.S(320), this.Row(i, top, 640).Y, this.S(640), this.S(140)), Ui.Muted);
            this.Back(i + 3, top);
        }
    }

    /// <summary>Host session plus scripted bot clients over an <see cref="InMemoryNetwork"/>.</summary>
    public sealed class DevGameSession : IGameSession
    {
        private readonly AbyssApp app;
        private readonly InMemoryNetwork net;
        private readonly InMemoryNetwork.Endpoint hostEndpoint;
        private readonly List<(ClientSession client, InMemoryNetwork.Endpoint ep, ParticipantIdentity id, AutoPilot bot)> bots = new();
        private readonly string runDir;
        private HostGameSession inner;
        private readonly HashSet<string> devKeysDown = new();

        private DevGameSession(AbyssApp app, InMemoryNetwork net, InMemoryNetwork.Endpoint hostEndpoint, HostGameSession inner, string runDir)
        {
            this.app = app;
            this.net = net;
            this.hostEndpoint = hostEndpoint;
            this.inner = inner;
            this.runDir = runDir;
        }

        public static DevGameSession Start(AbyssApp app, int level, int botCount, string difficulty, ulong seed, int latency)
        {
            var net = new InMemoryNetwork(seed);
            net.Conditions.LatencyTicks = latency;
            var ep = net.CreateEndpoint();
            string runRoot = app.RunsDirectory;
            var host = CreateHost(app, ep, runRoot);
            var session = new DevGameSession(app, net, ep, new HostGameSession(host, null), runRoot);
            for (int i = 0; i < botCount; i++)
            {
                var id = ParticipantIdentity.Create();
                var bep = net.CreateEndpoint();
                var client = new ClientSession(bep, app.Data, id, $"Bot {i + 1}", new[] { "warrior" }, app.Data.Balance.Scaling.Difficulties.Select(d => d.Id), app.AssetHash);
                session.bots.Add((client, bep, id, new AutoPilot((ulong)i + seed)));
                net.Connect(ep, bep);
            }
            for (int t = 0; t < 10; t++)
                session.PumpNetwork();
            foreach (var b in session.bots)
                b.client.SendAction(ClientAction.SetReady, flag: true);
            host.SetHostReady(true);
            host.SetDifficulty(difficulty);
            for (int t = 0; t < 10; t++)
                session.PumpNetwork();
            host.StartRun(seed, developer: true, developerStartLevel: level);
            return session;
        }

        private static HostSession CreateHost(AbyssApp app, INetTransport transport, string runRoot)
        {
            var all = app.Data.Balance.Scaling.Difficulties.Select(d => d.Id).ToList();
            var host = new HostSession(transport, app.Data, app.Identity.ParticipantId, app.Profile.DisplayName + " (Dev)", app.Data.Classes.Keys, all, app.AssetHash,
                run => new RunStore(Path.Combine(runRoot, run.RunId)));
            host.Log += m => app.Platform.Log("[dev] " + m);
            return host;
        }

        private void PumpNetwork()
        {
            this.net.Advance();
            this.inner.Host.Tick();
            foreach (var b in this.bots)
                b.client.Tick(Vec2.Zero, 0, InputButtons.None);
        }

        public bool IsHost => true;
        public WorldState? World => this.inner.World;
        public int OwnPlayerId => this.inner.OwnPlayerId;
        public string OwnParticipantId => this.inner.OwnParticipantId;
        public Vec2? OwnPositionOverride => null;
        public RunState? Run => this.inner.Run;
        public RunPhase? Phase => this.inner.Phase;
        public LobbyView? Lobby => this.inner.Lobby;
        public IReadOnlyList<string> PendingPactVoters => this.inner.PendingPactVoters;
        public string? Problem => null;

        public void Tick(Vec2 move, float aim, InputButtons held)
        {
            this.HandleDevKeys();
            this.net.Advance();
            this.inner.Tick(move, aim, held);
            foreach (var b in this.bots)
            {
                if (b.client.World != null && b.client.PlayerEntityId >= 0)
                {
                    var input = b.bot.Next(b.client.World, b.client.PlayerEntityId);
                    b.client.Tick(input.Move, input.AimRadians, input.Held);
                }
                else
                {
                    b.client.Tick(Vec2.Zero, 0, InputButtons.None);
                }
                // bots agree to the pact so the host's decision is what gets tested
                if (b.client.Phase == RunPhase.PactVote && b.client.PendingPactVoters.Contains(b.id.ParticipantId))
                    b.client.SendAction(ClientAction.PactVote, flag: true);
            }
        }

        private bool Edge(string key)
        {
            bool down = this.app.Platform.IsButtonDown(key);
            bool was = this.devKeysDown.Contains(key);
            if (down)
                this.devKeysDown.Add(key);
            else
                this.devKeysDown.Remove(key);
            return down && !was;
        }

        private void HandleDevKeys()
        {
            var world = this.inner.Host.Controller?.World;
            if (this.Edge("F5") && world != null)
            {
                var me = world.FindPlayerByParticipant(this.inner.OwnParticipantId);
                if (me != null)
                    me.Hp = me.MaxHp;
            }
            if (this.Edge("F6") && world != null)
            {
                foreach (var e in world.State.Enemies)
                    e.Hp = Math.Min(e.Hp, 0.01f);
            }
            if (this.Edge("F7") && this.bots.Count > 0)
            {
                // drop and reconnect the first bot with the same identity
                var b = this.bots[0];
                this.net.Cut(this.hostEndpoint, b.ep);
                var ep = this.net.CreateEndpoint();
                var client = new ClientSession(ep, this.app.Data, b.id, "Bot 1", new[] { "warrior" }, new[] { "normal" }, this.app.AssetHash);
                this.bots[0] = (client, ep, b.id, b.bot);
                this.net.Connect(this.hostEndpoint, ep);
            }
            if (this.Edge("F8") && this.inner.Host.Controller != null)
            {
                // simulated host crash: throw the host away and recover from disk like after a real crash
                string runId = this.inner.Host.Controller.Run.RunId;
                foreach (var b in this.bots)
                    this.net.Cut(this.hostEndpoint, b.ep);
                var recovered = new RunStore(Path.Combine(this.runDir, runId)).Load(new TransactionProcessor(this.app.Data));
                if (recovered != null)
                {
                    var host = CreateHost(this.app, this.hostEndpoint, this.runDir);
                    host.ResumeRun(recovered);
                    this.inner = new HostGameSession(host, null);
                    for (int i = 0; i < this.bots.Count; i++)
                    {
                        var b = this.bots[i];
                        var ep = this.net.CreateEndpoint();
                        var client = new ClientSession(ep, this.app.Data, b.id, $"Bot {i + 1}", new[] { "warrior" }, new[] { "normal" }, this.app.AssetHash);
                        this.bots[i] = (client, ep, b.id, b.bot);
                        this.net.Connect(this.hostEndpoint, ep);
                    }
                    this.app.Platform.Log("[dev] host crash simulated; " + string.Join("; ", recovered.Notes) + $"; replayed {recovered.ReplayedTransactions} tx");
                }
            }
            if (this.Edge("F9") && world != null)
            {
                var enemies = this.app.Data.Enemies.Values.Where(e => e.Biome == 1 && e.SpawnCost >= 0.5f).ToList();
                var spawns = world.State.Arena.SpawnPoints;
                for (int i = 0; i < 6 && spawns.Count > 0; i++)
                    world.SpawnEnemy(enemies[i % enemies.Count].Id, spawns[i % spawns.Count].Center, false);
            }
        }

        public List<SimEvent> DrainEvents() => this.inner.DrainEvents();
        public void SetReady(bool ready) => this.inner.SetReady(ready);
        public void SetClass(string classId) => this.inner.SetClass(classId);
        public void SetWeapon(string weaponTypeId) => this.inner.SetWeapon(weaponTypeId);
        public void ContinueToNextArena() => this.inner.ContinueToNextArena();
        public void VotePact(bool accept) => this.inner.VotePact(accept);
        public void Dispose() => this.inner.Dispose();
    }
}
