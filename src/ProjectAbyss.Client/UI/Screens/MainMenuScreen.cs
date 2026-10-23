using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ProjectAbyss.Client.UI.Screens
{
    /// <summary>Project Abyss main menu (design §24). Every entry is functional; "Normales Stardew Valley" opens the vanilla title.</summary>
    public sealed class MainMenuScreen : Screen
    {
        private bool hasResumable;

        public MainMenuScreen(AbyssApp app) : base(app)
        {
        }

        public override void OnEnter() => this.hasResumable = this.App.FindResumableRuns().Count > 0;

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            this.Ui.Title("PROJECT ABYSS", this.S(110), s);
            Rendering.Draw.TextCentered(b, this.Ui.Small, "Ein düsteres Action-Roguelite", new Vector2(s.X / 2f, this.S(170)), Ui.Muted, this.Ui.Scale);
            int top = 220;
            int i = 0;
            if (this.Ui.Button(this.Row(i++, top), "Neuer Durchlauf"))
                this.Go(new NewRunScreen(this.App, multiplayerHost: false));
            if (this.Ui.Button(this.Row(i++, top), "Run fortsetzen", this.hasResumable))
                this.Go(new ResumeScreen(this.App));
            if (this.Ui.Button(this.Row(i++, top), "Mehrspieler"))
                this.Go(new MultiplayerScreen(this.App));
            if (this.Ui.Button(this.Row(i++, top), "Klassen und Freischaltungen"))
                this.Go(new ClassesScreen(this.App));
            if (this.Ui.Button(this.Row(i++, top), "Archiv / Bestiarium"))
                this.Go(new ArchiveScreen(this.App));
            if (this.Ui.Button(this.Row(i++, top), "Statistiken"))
                this.Go(new StatsScreen(this.App));
            if (this.Ui.Button(this.Row(i++, top), "Einstellungen"))
                this.Go(new SettingsScreen(this.App));
            if (this.App.Platform.DeveloperMode && this.Ui.Button(this.Row(i++, top), "Entwicklermodus"))
                this.Go(new DeveloperScreen(this.App));
            if (this.Ui.Button(this.Row(i++, top), "Normales Stardew Valley"))
            {
                this.App.UiClick();
                this.App.Platform.OpenVanillaStardew();
            }
            if (this.Ui.Button(this.Row(i++, top), "Spiel beenden"))
                this.App.Platform.QuitGame();
            if (this.App.Profile.IsDeveloperProfile)
                this.Ui.Label("ENTWICKLERPROFIL – keine Erfolge oder Freischaltungen", new Vector2(this.S(20), s.Y - this.S(40)), Color.Orange);
            this.Ui.Label($"v{typeof(MainMenuScreen).Assembly.GetName().Version}", new Vector2(s.X - this.S(120), s.Y - this.S(40)), Ui.Muted);
        }

        private void Go(Screen next)
        {
            this.App.UiClick();
            this.App.Show(next);
        }
    }

    public sealed class ResumeScreen : Screen
    {
        public ResumeScreen(AbyssApp app) : base(app)
        {
        }

        public override void Render(SpriteBatch b)
        {
            this.Ui.Title("Run fortsetzen", this.S(110), this.ScreenSize);
            var runs = this.App.FindResumableRuns();
            int i = 0;
            foreach (var (dir, run) in runs.Take(6))
            {
                string label = $"Ebene {run.CurrentLevel} · {run.Participants.Count} Spieler · {run.DifficultyId} · {run.CreatedUtc.ToLocalTime():g}";
                if (this.Ui.Button(this.Row(i++, 200, 760), label))
                    this.App.Show(GameScreen.ResumeHosted(this.App, dir));
            }
            if (runs.Count == 0)
                this.Ui.Label("Kein laufender Run gefunden.", new Vector2(this.ScreenSize.X / 2f - this.S(150), this.S(220)));
            this.Back(i + 1, 200);
        }
    }
}
