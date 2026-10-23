using System;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Client.Session;
using ProjectAbyss.Core.Net;
using ProjectAbyss.Core.Persistence;

namespace ProjectAbyss.Client.UI.Screens
{
    /// <summary>Class, weapon and difficulty selection for a new single-player run (or for hosting a co-op lobby).</summary>
    public sealed class NewRunScreen : Screen
    {
        /// <summary>The six classes of the design; only those present in the game data are playable.</summary>
        public static readonly (string id, string name)[] DesignClasses =
        {
            ("warrior", "Krieger"), ("mage", "Magier"), ("ranger", "Waldläufer"), ("assassin", "Assassine"), ("berserker", "Berserker"), ("paladin", "Paladin"),
        };

        private readonly bool multiplayerHost;
        private string classId;
        private string weaponId;
        private string difficultyId = "normal";
        private string? error;

        public NewRunScreen(AbyssApp app, bool multiplayerHost) : base(app)
        {
            this.multiplayerHost = multiplayerHost;
            this.classId = app.Data.Classes.Keys.First();
            this.weaponId = app.Data.Classes[this.classId].WeaponTypes[0];
        }

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            this.Ui.Title(this.multiplayerHost ? "Koop-Lobby erstellen" : "Neuer Durchlauf", this.S(90), s);
            int x0 = s.X / 2 - this.S(520);
            int y = this.S(170);
            this.Ui.Label("Klasse", new Vector2(x0, y), Ui.Muted);
            for (int i = 0; i < DesignClasses.Length; i++)
            {
                var (id, name) = DesignClasses[i];
                bool implemented = this.App.Data.Classes.ContainsKey(id);
                bool unlocked = this.App.Profile.UnlockedClasses.Contains(id);
                string label = !implemented ? name + " (in Entwicklung)" : unlocked ? name : name + " (gesperrt)";
                var r = new Rectangle(x0 + (i % 3) * this.S(350), y + this.S(30) + (i / 3) * this.S(62), this.S(330), this.S(52));
                if (this.Ui.Button(r, label, implemented && unlocked, this.classId == id))
                {
                    this.classId = id;
                    this.weaponId = this.App.Data.Classes[id].WeaponTypes[0];
                }
            }
            y += this.S(180);
            this.Ui.Label("Waffe", new Vector2(x0, y), Ui.Muted);
            var weapons = this.App.Data.Classes[this.classId].WeaponTypes;
            for (int i = 0; i < weapons.Count; i++)
            {
                var w = this.App.Data.WeaponTypes[weapons[i]];
                var r = new Rectangle(x0 + i * this.S(350), y + this.S(30), this.S(330), this.S(52));
                if (this.Ui.Button(r, w.Name, true, this.weaponId == w.Id))
                    this.weaponId = w.Id;
            }
            var wdef = this.App.Data.WeaponTypes[this.weaponId];
            this.Ui.Paragraph(wdef.PassiveDescription, new Rectangle(x0, y + this.S(92), this.S(1040), this.S(60)), Ui.Muted);
            y += this.S(160);
            this.Ui.Label("Schwierigkeit", new Vector2(x0, y), Ui.Muted);
            var diffs = this.App.Data.Balance.Scaling.Difficulties;
            for (int i = 0; i < diffs.Count; i++)
            {
                bool unlocked = this.App.Profile.UnlockedDifficulties.Contains(diffs[i].Id);
                var r = new Rectangle(x0 + i * this.S(262), y + this.S(30), this.S(250), this.S(52));
                if (this.Ui.Button(r, unlocked ? diffs[i].Name : diffs[i].Name + " (gesperrt)", unlocked, this.difficultyId == diffs[i].Id))
                    this.difficultyId = diffs[i].Id;
            }
            y += this.S(110);
            if (this.error != null)
                this.Ui.Label(this.error, new Vector2(x0, y), Color.OrangeRed);
            if (this.Ui.Button(new Rectangle(s.X / 2 - this.S(210), y + this.S(40), this.S(420), this.S(56)), this.multiplayerHost ? "Lobby öffnen" : "Abstieg beginnen"))
                this.Start();
            if (this.Ui.Button(new Rectangle(s.X / 2 - this.S(210), y + this.S(106), this.S(420), this.S(52)), "Zurück"))
                this.App.Show(this.multiplayerHost ? new MultiplayerScreen(this.App) : new MainMenuScreen(this.App));
        }

        private void Start()
        {
            this.App.UiClick();
            TcpTransport? tcp = null;
            INetTransport transport = new NullTransport();
            if (this.multiplayerHost)
            {
                try
                {
                    tcp = TcpTransport.Listen(IPAddress.Any, MultiplayerScreen.Port);
                    transport = tcp;
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    this.error = "Port konnte nicht geöffnet werden: " + ex.Message;
                    return;
                }
            }
            var host = new HostSession(transport, this.App.Data, this.App.Identity.ParticipantId, this.App.Profile.DisplayName,
                this.App.Profile.UnlockedClasses, this.App.Profile.UnlockedDifficulties, this.App.AssetHash,
                run => new RunStore(Path.Combine(this.App.RunsDirectory, run.RunId)));
            host.Log += m => this.App.Platform.Log(m);
            host.SetHostClass(this.classId);
            host.SetHostWeapon(this.weaponId);
            host.SetDifficulty(this.difficultyId);
            var session = new HostGameSession(host, tcp);
            if (this.multiplayerHost)
            {
                this.App.Show(new LobbyScreen(this.App, session));
                return;
            }
            host.SetHostReady(true);
            host.StartRun(NewSeed(), developer: this.App.Profile.IsDeveloperProfile);
            this.App.Profile.Statistics.RunsStarted++;
            this.App.ProfileStore.Save();
            this.App.Show(new GameScreen(this.App, session));
        }

        public static ulong NewSeed()
        {
            var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
            return BitConverter.ToUInt64(bytes, 0);
        }
    }
}
