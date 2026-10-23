using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ProjectAbyss.Client.UI.Screens
{
    /// <summary>Volumes, accessibility, free key binding, and local data export/deletion (design §32, §35).</summary>
    public sealed class SettingsScreen : Screen
    {
        private string? rebinding;
        private string? message;
        private bool confirmDelete;

        public SettingsScreen(AbyssApp app) : base(app)
        {
        }

        public override void Update()
        {
            if (this.rebinding == null)
                return;
            string? key = this.App.Platform.ConsumePressedButton();
            if (key == null)
                return;
            if (key != "Escape" || this.rebinding == "menu")
                this.App.Profile.Settings.KeyBindings[this.rebinding] = key;
            this.rebinding = null;
            this.App.ProfileStore.Save();
        }

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            var settings = this.App.Profile.Settings;
            this.Ui.Title("Einstellungen", this.S(70), s);
            int x = s.X / 2 - this.S(620);
            int y = this.S(130);
            int w = this.S(380);
            int h = this.S(44);

            this.Ui.Label("Lautstärke", new Vector2(x, y), Ui.Accent);
            y += this.S(36);
            var audio = settings.Audio;
            audio.Master = this.Slider("Gesamt", audio.Master, x, ref y, w, h);
            audio.Music = this.Slider("Musik", audio.Music, x, ref y, w, h);
            audio.Effects = this.Slider("Effekte", audio.Effects, x, ref y, w, h);
            audio.Ambience = this.Slider("Umgebung", audio.Ambience, x, ref y, w, h);

            y += this.S(10);
            this.Ui.Label("Barrierefreiheit", new Vector2(x, y), Ui.Accent);
            y += this.S(36);
            var a = settings.Accessibility;
            a.HudScale = MathF.Round(this.Slider("HUD-Skalierung", (a.HudScale - 0.5f) / 1.5f, x, ref y, w, h) * 1.5f + 0.5f, 2);
            a.ReducedParticles = this.Toggle("Weniger Partikel", a.ReducedParticles, x, ref y, w, h);
            a.ScreenShake = this.Toggle("Kamerawackeln", a.ScreenShake, x, ref y, w, h);
            a.ReducedFlashes = this.Toggle("Reduzierte Blitzeffekte", a.ReducedFlashes, x, ref y, w, h);
            a.HighContrastTelegraphs = this.Toggle("Kontrastreiche Warnflächen", a.HighContrastTelegraphs, x, ref y, w, h);
            a.ShowDamageNumbers = this.Toggle("Schadenszahlen", a.ShowDamageNumbers, x, ref y, w, h);

            // key bindings (right column)
            int kx = s.X / 2 + this.S(40);
            int ky = this.S(130);
            this.Ui.Label("Steuerung (anklicken, dann Taste drücken)", new Vector2(kx, ky), Ui.Accent);
            ky += this.S(36);
            foreach (var bind in settings.KeyBindings.ToList())
            {
                string label = $"{ActionName(bind.Key)}: {(this.rebinding == bind.Key ? "…" : bind.Value)}";
                if (this.Ui.Button(new Rectangle(kx, ky, this.S(560), this.S(38)), label, true, this.rebinding == bind.Key))
                    this.rebinding = bind.Key;
                ky += this.S(42);
            }
            if (this.Ui.Button(new Rectangle(kx, ky + this.S(6), this.S(560), this.S(40)), "Standardbelegung"))
                settings.KeyBindings = Core.Profile.ProfileSettings.DefaultBindings();

            // data protection
            int by = Math.Max(y, ky) + this.S(60);
            if (this.Ui.Button(new Rectangle(x, by, this.S(300), h), "Profildaten exportieren"))
            {
                string path = Path.Combine(this.App.Platform.DataDirectory, $"profile-export-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(path, this.App.ProfileStore.ExportJson());
                this.message = "Exportiert nach " + path;
            }
            if (this.Ui.Button(new Rectangle(x + this.S(320), by, this.S(300), h), this.confirmDelete ? "Wirklich löschen?" : "Profildaten löschen", true, this.confirmDelete))
            {
                if (this.confirmDelete)
                {
                    this.App.ProfileStore.DeleteAll();
                    this.App.ProfileStore.LoadOrCreate(this.App.Data, "Spieler");
                    this.message = "Lokale Profildaten wurden gelöscht.";
                    this.confirmDelete = false;
                }
                else
                {
                    this.confirmDelete = true;
                }
            }
            if (this.message != null)
                this.Ui.Label(this.message, new Vector2(x, by + this.S(54)), Ui.Muted);
            if (this.Ui.Button(new Rectangle(s.X / 2 - this.S(200), by + this.S(100), this.S(400), h), "Speichern und zurück"))
            {
                this.App.ProfileStore.Save();
                this.App.Show(new MainMenuScreen(this.App));
            }
        }

        private float Slider(string name, float value, int x, ref int y, int w, int h)
        {
            this.Ui.Label($"{name}: {value * 100:0} %", new Vector2(x, y + this.S(8)));
            if (this.Ui.Button(new Rectangle(x + w, y, this.S(50), h), "−"))
                value -= 0.1f;
            if (this.Ui.Button(new Rectangle(x + w + this.S(60), y, this.S(50), h), "+"))
                value += 0.1f;
            y += h + this.S(6);
            return Math.Clamp(MathF.Round(value, 2), 0f, 1f);
        }

        private bool Toggle(string name, bool value, int x, ref int y, int w, int h)
        {
            if (this.Ui.Button(new Rectangle(x, y, w + this.S(110), h), $"{name}: {(value ? "an" : "aus")}", true, value))
                value = !value;
            y += h + this.S(6);
            return value;
        }

        private static string ActionName(string key) => key switch
        {
            "attack" => "Grundangriff",
            "special" => "Spezialangriff",
            "ability1" => "Fähigkeit 1",
            "ability2" => "Fähigkeit 2",
            "ultimate" => "Ultimate",
            "dash" => "Dash",
            "potion" => "Heiltrank",
            "skillTree" => "Skillbaum",
            "inventory" => "Inventar",
            "map" => "Karte",
            "interact" => "Interagieren / Wiederbeleben",
            "moveUp" => "Hoch",
            "moveDown" => "Runter",
            "moveLeft" => "Links",
            "moveRight" => "Rechts",
            "menu" => "Menü / Pause",
            _ => key,
        };
    }
}
