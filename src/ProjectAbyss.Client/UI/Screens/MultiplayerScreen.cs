using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Client.Session;
using ProjectAbyss.Core.Net;

namespace ProjectAbyss.Client.UI.Screens
{
    /// <summary>
    /// Co-op entry. Version 1 uses direct TCP connections (LAN / port forwarding). Whether co-op should instead run
    /// through Stardew's own multiplayer (Steam/GOG invites) is an open decision, see docs/KNOWN_LIMITATIONS.md.
    /// </summary>
    public sealed class MultiplayerScreen : Screen
    {
        public const int Port = AbyssApp.DefaultPort;
        private string address = "127.0.0.1";
        private bool editing;
        private string? error;

        public MultiplayerScreen(AbyssApp app) : base(app)
        {
        }

        public override void Update()
        {
            if (!this.editing)
                return;
            string? key = this.App.Platform.ConsumePressedButton();
            if (key == null)
                return;
            if (key is "Enter" or "Escape")
                this.editing = false;
            else if (key == "Back" && this.address.Length > 0)
                this.address = this.address.Substring(0, this.address.Length - 1);
            else if (this.address.Length < 64)
            {
                char? c = KeyToChar(key);
                if (c != null)
                    this.address += c.Value;
            }
        }

        public static char? KeyToChar(string key)
        {
            if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]))
                return key[1];
            if (key.StartsWith("NumPad", StringComparison.Ordinal) && key.Length == 7 && char.IsDigit(key[6]))
                return key[6];
            if (key is "OemPeriod" or "Decimal")
                return '.';
            if (key == "OemMinus" || key == "Subtract")
                return '-';
            if (key.Length == 1 && char.IsLetter(key[0]))
                return char.ToLowerInvariant(key[0]);
            return key == "OemSemicolon" ? ':' : null;
        }

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            this.Ui.Title("Mehrspieler", this.S(110), s);
            this.Ui.Paragraph("Koop für 2–4 Spieler. Der Host simuliert den Kampf verbindlich. Verbindung über direkte IP (LAN oder weitergeleiteter TCP-Port " + Port + ").",
                new Rectangle(s.X / 2 - this.S(420), this.S(170), this.S(840), this.S(80)), Ui.Muted);
            if (this.Ui.Button(this.Row(0, 270), "Lobby erstellen (Host)"))
                this.App.Show(new NewRunScreen(this.App, multiplayerHost: true));
            var field = this.Row(1, 290);
            this.Ui.PanelBox(field);
            this.Ui.Label("Adresse: " + this.address + (this.editing && (Environment.TickCount / 400) % 2 == 0 ? "_" : ""), new Vector2(field.X + this.S(12), field.Y + this.S(12)));
            if (this.Ui.Button(new Rectangle(field.Right + this.S(10), field.Y, this.S(140), field.Height), this.editing ? "Fertig" : "Ändern"))
                this.editing = !this.editing;
            if (this.Ui.Button(this.Row(2, 290), "Beitreten"))
                this.Join();
            if (this.error != null)
                this.Ui.Label(this.error, new Vector2(field.X, this.Row(3, 290).Y), Color.OrangeRed);
            this.Back(4, 300);
        }

        private void Join()
        {
            this.App.UiClick();
            try
            {
                var tcp = TcpTransport.Connect(this.address.Trim(), Port);
                var client = new ClientSession(tcp, this.App.Data, this.App.Identity, this.App.Profile.DisplayName,
                    this.App.Profile.UnlockedClasses, this.App.Profile.UnlockedDifficulties, this.App.AssetHash);
                client.Log += m => this.App.Platform.Log(m);
                this.App.Show(new LobbyScreen(this.App, new ClientGameSession(client, tcp, this.App.Identity.ParticipantId)));
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
            {
                this.error = "Verbindung fehlgeschlagen: " + ex.Message;
            }
        }
    }

    public sealed class LobbyScreen : Screen
    {
        private readonly IGameSession session;
        private bool ready;
        private string? error;

        public LobbyScreen(AbyssApp app, IGameSession session) : base(app)
        {
            this.session = session;
        }

        public override void Update()
        {
            this.session.Tick(Core.Mathematics.Vec2.Zero, 0, Core.Simulation.InputButtons.None);
            if (this.session.Phase != null)
                this.App.Show(new GameScreen(this.App, this.session));
        }

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            this.Ui.Title("Lobby", this.S(100), s);
            if (this.session.Problem != null)
            {
                this.Ui.Label(this.session.Problem, new Vector2(s.X / 2f - this.S(300), this.S(200)), Color.OrangeRed);
                if (this.Ui.Button(this.Row(2, 260), "Zurück"))
                {
                    this.session.Dispose();
                    this.App.Show(new MultiplayerScreen(this.App));
                }
                return;
            }
            var lobby = this.session.Lobby;
            if (lobby == null)
            {
                this.Ui.Label("Verbinde …", new Vector2(s.X / 2f - this.S(60), this.S(200)));
                return;
            }
            var panel = new Rectangle(s.X / 2 - this.S(460), this.S(170), this.S(920), this.S(300));
            this.Ui.PanelBox(panel);
            int y = panel.Y + this.S(16);
            foreach (var m in lobby.Members)
            {
                string cls = this.App.Data.Classes.TryGetValue(m.ClassId, out var c) ? c.Name : m.ClassId;
                string weapon = this.App.Data.WeaponTypes.TryGetValue(m.WeaponTypeId, out var w) ? w.Name : "";
                this.Ui.Label($"{(m.IsHost ? "[Host] " : "")}{m.DisplayName}  –  {cls} ({weapon})  –  {(m.Ready ? "bereit" : "nicht bereit")}",
                    new Vector2(panel.X + this.S(20), y), m.Ready ? Color.LightGreen : Ui.TextColor);
                y += this.S(56);
            }
            this.Ui.Label("Schwierigkeit: " + lobby.DifficultyId, new Vector2(panel.X + this.S(20), panel.Bottom - this.S(40)), Ui.Muted);
            int row = 0;
            int top = 500;
            if (this.Ui.Button(this.Row(row++, top), this.ready ? "Nicht bereit" : "Bereit"))
            {
                this.ready = !this.ready;
                this.session.SetReady(this.ready);
            }
            if (this.session is HostGameSession host)
            {
                var blockers = host.Host.StartBlockers();
                if (this.Ui.Button(this.Row(row++, top), "Run starten", blockers.Count == 0))
                {
                    try
                    {
                        host.Host.StartRun(NewRunScreen.NewSeed(), developer: this.App.Profile.IsDeveloperProfile);
                    }
                    catch (InvalidOperationException ex)
                    {
                        this.error = ex.Message;
                    }
                }
                if (blockers.Count > 0)
                    this.Ui.Label(blockers[0], new Vector2(this.Row(row, top).X, this.Row(row, top).Y), Ui.Muted);
                row++;
            }
            if (this.error != null)
                this.Ui.Label(this.error, new Vector2(this.Row(row, top).X, this.Row(row, top).Y), Color.OrangeRed);
            if (this.Ui.Button(this.Row(row + 1, top), "Lobby verlassen"))
            {
                this.session.Dispose();
                this.App.Show(new MainMenuScreen(this.App));
            }
        }
    }
}
