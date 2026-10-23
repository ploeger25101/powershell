using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Client.Rendering;
using ProjectAbyss.Client.Session;
using ProjectAbyss.Core.Mathematics;
using ProjectAbyss.Core.Net;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Profile;
using ProjectAbyss.Core.Run;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Client.UI.Screens
{
    /// <summary>Arena view, HUD and the between-arena / pact / run-end overlays.</summary>
    public sealed class GameScreen : Screen
    {
        private readonly IGameSession session;
        private readonly Camera camera = new();
        private readonly FloatingTexts texts = new();
        private readonly Hud hud;
        private bool paused;
        private bool menuWasDown;
        private bool runEndHandled;
        private int shakeTicks;
        private string? banner;
        private int bannerTicks;
        private double lastTickMs;

        public GameScreen(AbyssApp app, IGameSession session) : base(app)
        {
            this.session = session;
            this.hud = new Hud(app);
        }

        /// <summary>Continue a saved run as host (single player, or co-op host waiting for its original players).</summary>
        public static Screen ResumeHosted(AbyssApp app, string runDirectory)
        {
            var recovered = new RunStore(runDirectory).Load(new TransactionProcessor(app.Data));
            if (recovered == null)
                return new MainMenuScreen(app);
            foreach (string note in recovered.Notes)
                app.Platform.Log("recovery: " + note);
            TcpTransport? tcp = null;
            INetTransport transport = new NullTransport();
            if (recovered.Run.Participants.Count > 1)
            {
                tcp = TcpTransport.Listen(System.Net.IPAddress.Any, MultiplayerScreen.Port);
                transport = tcp;
            }
            var host = new HostSession(transport, app.Data, app.Identity.ParticipantId, app.Profile.DisplayName, app.Profile.UnlockedClasses, app.Profile.UnlockedDifficulties,
                app.AssetHash, run => new RunStore(runDirectory));
            host.Log += m => app.Platform.Log(m);
            host.ResumeRun(recovered);
            return new GameScreen(app, new HostGameSession(host, tcp));
        }

        private ProfileSettings Settings => this.App.Profile.Settings;

        private bool Bound(string action) =>
            this.Settings.KeyBindings.TryGetValue(action, out var key) && this.App.Platform.IsButtonDown(key);

        public override void Update()
        {
            bool menuDown = this.Bound("menu");
            if (menuDown && !this.menuWasDown)
                this.paused = !this.paused;
            this.menuWasDown = menuDown;

            var world = this.session.World;
            var move = Vec2.Zero;
            float aim = 0;
            var held = InputButtons.None;
            if (!this.paused && world != null)
            {
                move = new Vec2((this.Bound("moveRight") ? 1 : 0) - (this.Bound("moveLeft") ? 1 : 0), (this.Bound("moveDown") ? 1 : 0) - (this.Bound("moveUp") ? 1 : 0));
                var own = world.Players.Find(p => p.Id == this.session.OwnPlayerId);
                var ownPos = this.session.OwnPositionOverride ?? own?.Position ?? Vec2.Zero;
                aim = (this.camera.ToWorld(this.App.Platform.MousePosition) - ownPos).Angle;
                if (this.Bound("attack")) held |= InputButtons.Attack;
                if (this.Bound("special")) held |= InputButtons.Special;
                if (this.Bound("ability1")) held |= InputButtons.Ability1;
                if (this.Bound("ability2")) held |= InputButtons.Ability2;
                if (this.Bound("ultimate")) held |= InputButtons.Ultimate;
                if (this.Bound("dash")) held |= InputButtons.Dash;
                if (this.Bound("potion")) held |= InputButtons.Potion;
                if (this.Bound("interact")) held |= InputButtons.Interact;
            }

            // single player pauses the simulation; in co-op the world keeps running for everyone else
            bool simulate = !(this.paused && this.session.IsHost && (this.session.Run?.Participants.Count ?? 1) == 1);
            if (simulate)
            {
                long start = Stopwatch.GetTimestamp();
                this.session.Tick(move, aim, held);
                this.lastTickMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            this.HandleEvents();
            this.texts.Update();
            if (this.bannerTicks > 0 && --this.bannerTicks == 0)
                this.banner = null;
            if (this.session.Phase == RunPhase.Finished && !this.runEndHandled)
                this.OnRunEnded();
        }

        private void HandleEvents()
        {
            var events = this.session.DrainEvents();
            if (events.Count == 0)
                return;
            int own = this.session.OwnPlayerId;
            var world = this.session.World;
            foreach (var r in this.App.Audio.SoundsFor(events, world?.Tick ?? 0, own))
                this.App.PlaySound(r);
            var access = this.Settings.Accessibility;
            var stats = this.App.Profile.Statistics;
            foreach (var e in events)
            {
                switch (e.Type)
                {
                    case SimEventType.Hit when access.ShowDamageNumbers:
                        this.texts.Add($"{e.Amount:0}", e.Position, e.Flag ? new Color(255, 220, 90) : Color.White, e.Flag ? 1.2f : 0.9f);
                        break;
                    case SimEventType.PlayerHit when e.TargetId == own:
                        if (access.ShowDamageNumbers)
                            this.texts.Add($"-{e.Amount:0}", e.Position, new Color(255, 90, 90));
                        if (access.ScreenShake)
                            this.shakeTicks = 8;
                        break;
                    case SimEventType.Parried:
                        this.texts.Add("Parade!", e.Position, Color.LightSkyBlue);
                        break;
                    case SimEventType.EnemyKilled:
                        stats.EnemiesKilled++;
                        this.App.Profile.Bestiary[e.Key] = this.App.Profile.Bestiary.GetValueOrDefault(e.Key) + 1;
                        if (e.Flag)
                            stats.BossesKilled++;
                        break;
                    case SimEventType.BossPhaseChanged when e.IntValue > 0:
                        this.ShowBanner($"Phase {e.IntValue + 1}");
                        break;
                    case SimEventType.BossStaggered:
                        this.ShowBanner("Taumelnd!");
                        break;
                    case SimEventType.WaveStarted:
                        this.ShowBanner($"Welle {e.IntValue + 1}");
                        break;
                    case SimEventType.ArenaCleared:
                        this.ShowBanner("Arena gesäubert");
                        if (world != null)
                            stats.DeepestLevel = Math.Max(stats.DeepestLevel, world.Level);
                        this.App.ProfileStore.Save();
                        break;
                }
            }
        }

        private void ShowBanner(string text)
        {
            this.banner = text;
            this.bannerTicks = 120;
        }

        /// <summary>Permanent consequences are applied to the local profile only (each player owns their profile).</summary>
        private void OnRunEnded()
        {
            this.runEndHandled = true;
            var run = this.session.Run;
            if (run == null)
                return;
            var stats = this.App.Profile.Statistics;
            if (run.Status == RunStatus.Won)
            {
                stats.RunsWon++;
                var diffs = this.App.Data.Balance.Scaling.Difficulties;
                var next = diffs.FirstOrDefault(d => d.UnlockedBy == run.DifficultyId);
                if (next != null && !run.IsDeveloperRun)
                    this.App.ProfileStore.Unlock($"difficulty:{next.Id}:{run.RunId}", UnlockKind.Difficulty, next.Id);
            }
            else
            {
                stats.RunsLost++;
            }
            this.App.ProfileStore.Save();
            if (this.session is HostGameSession host && host.Host.Controller != null)
            {
                // permadeath: the run's temporary progress is discarded for good
                string dir = Path.Combine(this.App.RunsDirectory, run.RunId);
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);
                }
                catch (IOException ex)
                {
                    this.App.Platform.Log("could not delete finished run: " + ex.Message, error: true);
                }
            }
        }

        public override string? DesiredMusic() => this.App.Audio.DesiredMusic(this.session.Phase, this.session.World, this.session.Run);

        public override void Render(SpriteBatch b)
        {
            var screen = this.ScreenSize;
            if (this.session.Problem != null)
            {
                this.Overlay("Verbindung", this.session.Problem, () =>
                {
                    if (this.Ui.Button(this.Row(0, 520), "Zum Hauptmenü"))
                        this.Leave();
                });
                return;
            }
            var world = this.session.World;
            if (world != null && this.session.Phase == RunPhase.InArena)
            {
                var own = world.Players.Find(p => p.Id == this.session.OwnPlayerId);
                var focus = this.session.OwnPositionOverride ?? own?.Position ?? new Vec2(world.Arena.PixelWidth / 2, world.Arena.PixelHeight / 2);
                this.camera.Update(screen, world.Arena.PixelWidth, world.Arena.PixelHeight, focus, 1f);
                this.camera.Shake = this.shakeTicks-- > 0 ? new Vector2((this.shakeTicks % 2 == 0 ? 1 : -1) * 3, 0) : Vector2.Zero;
                this.App.WorldRenderer.Render(b, world, this.camera, this.session.OwnPlayerId, this.session.OwnPositionOverride, this.Settings.Accessibility, world.Tick);
                this.texts.Render(b, this.Ui.Small, this.camera);
                this.hud.Render(b, world, this.session.OwnPlayerId, this.banner);
                // crosshair at the exact aim point
                var m = this.App.Platform.MousePosition;
                this.Ui.Draw.Ring(b, m.ToVector2(), this.S(9), new Color(240, 230, 200), 2f);
                if (this.App.Platform.DeveloperMode)
                    Draw.Text(b, this.Ui.Small, $"tick {world.Tick}  sim {this.lastTickMs:0.00} ms  enemies {world.Enemies.Count}  projectiles {world.Projectiles.Count}  seed {world.Arena.Seed}",
                        new Vector2(this.S(20), screen.Y - this.S(170)), Color.Orange, this.Ui.Scale * 0.7f);
            }
            else
            {
                this.RenderBetween(b);
            }
            if (this.paused)
                this.RenderPause();
        }

        private void RenderBetween(SpriteBatch b)
        {
            var run = this.session.Run;
            switch (this.session.Phase)
            {
                case RunPhase.BetweenArenas when run != null:
                    this.Overlay($"Ebene {run.CurrentLevel - 1} abgeschlossen", this.RunSummary(run) + (RunPlan.HasMerchant(run.CurrentLevel - 1)
                        ? "\nHändlerstation: Handel, Schmiede und Runen folgen in Phase 3 (siehe Entwicklungsstand)." : ""), () =>
                    {
                        if (this.session.IsHost)
                        {
                            if (this.Ui.Button(this.Row(0, 640), $"Weiter zu Ebene {run.CurrentLevel}"))
                                this.session.ContinueToNextArena();
                        }
                        else
                        {
                            this.Ui.Label("Warte auf den Host …", new Vector2(this.Row(0, 640).X, this.Row(0, 640).Y));
                        }
                        if (this.Ui.Button(this.Row(1, 640), "Speichern und zum Hauptmenü"))
                            this.Leave();
                    });
                    break;
                case RunPhase.PactVote when run != null:
                    string pending = string.Join(", ", this.session.PendingPactVoters.Select(id => run.Find(id)?.DisplayName ?? id));
                    this.Overlay("Der Pakt der letzten Chance",
                        "Eure Gruppe ist gefallen. Einmal pro Run könnt ihr denselben Boss erneut herausfordern: Alle ursprünglichen Teilnehmer kehren mit genau einem Leben zurück, " +
                        "das gesamte Gold geht verloren, der Boss startet mit vollen Lebenspunkten und erhält eine zusätzliche Angriffsmechanik. Alle müssen zustimmen; fehlende Spieler pausieren die Abstimmung." +
                        (pending.Length > 0 ? "\n\nAusstehende Stimmen: " + pending : ""), () =>
                    {
                        bool canVote = this.session.PendingPactVoters.Contains(this.session.OwnParticipantId);
                        if (this.Ui.Button(this.Row(0, 640), "Pakt annehmen", canVote))
                            this.session.VotePact(true);
                        if (this.Ui.Button(this.Row(1, 640), "Ablehnen (Run endet)", canVote))
                            this.session.VotePact(false);
                        if (this.Ui.Button(this.Row(2, 640), "Speichern und zum Hauptmenü"))
                            this.Leave();
                    });
                    break;
                case RunPhase.Finished when run != null:
                    this.Overlay(run.Status == RunStatus.Won ? "Der Abgrund ist bezwungen" : "Der Abgrund hat euch verschlungen",
                        this.RunSummary(run) + "\nRun-Fortschritt, Ausrüstung und Gold sind verfallen. Dauerhafte Freischaltungen bleiben erhalten.", () =>
                    {
                        if (this.Ui.Button(this.Row(0, 640), "Zum Hauptmenü"))
                            this.Leave();
                    });
                    break;
                default:
                    Draw.TextCentered(b, this.Ui.Large, "Lade …", this.ScreenSize.ToVector2() / 2f, Ui.TextColor, this.Ui.Scale);
                    break;
            }
        }

        private string RunSummary(RunState run)
        {
            var me = run.Find(this.session.OwnParticipantId);
            if (me == null)
                return "";
            int items = me.Inventory.Items.Count();
            return $"Stufe {me.Level} ({me.Xp} EP) · Gold {me.Gold} · Leben {me.Lives} · Gegenstände {items}/{me.Inventory.Size}" +
                (me.PendingRewards.Count > 0 ? $" · {me.PendingRewards.Count} Belohnungen warten auf freien Platz" : "") +
                (me.UnspentSkillPoints > 0 ? $"\n{me.UnspentSkillPoints} Skillpunkte verfügbar (Skillbaum folgt in Phase 3/4)." : "");
        }

        private void Overlay(string title, string text, Action buttons)
        {
            var s = this.ScreenSize;
            this.Ui.Draw.Rect(this.Ui.Batch, new Rectangle(0, 0, s.X, s.Y), Color.Black * 0.7f);
            var panel = new Rectangle(s.X / 2 - this.S(560), this.S(140), this.S(1120), this.S(460));
            this.Ui.PanelBox(panel);
            Draw.TextCentered(this.Ui.Batch, this.Ui.Large, title, new Vector2(s.X / 2f, panel.Y + this.S(50)), Ui.Accent, this.Ui.Scale * 1.2f);
            this.Ui.Paragraph(text, new Rectangle(panel.X + this.S(40), panel.Y + this.S(110), panel.Width - this.S(80), panel.Height - this.S(130)));
            buttons();
        }

        private void RenderPause()
        {
            this.Overlay("Pause", this.session.IsHost && (this.session.Run?.Participants.Count ?? 1) == 1
                ? "Das Spiel ist pausiert. Der Fortschritt wird an sicheren Übergängen und im Kampf laufend für die Absturz-Wiederherstellung gesichert."
                : "Im Koop läuft der Kampf weiter.", () =>
            {
                if (this.Ui.Button(this.Row(0, 640), "Fortsetzen"))
                    this.paused = false;
                if (this.Ui.Button(this.Row(1, 640), "Speichern und zum Hauptmenü"))
                    this.Leave();
            });
        }

        private void Leave()
        {
            if (this.session is HostGameSession host)
                host.Host.Controller?.SaveRecoverySnapshot();
            this.session.Dispose();
            this.App.ProfileStore.Save();
            this.App.Show(new MainMenuScreen(this.App));
        }
    }
}
