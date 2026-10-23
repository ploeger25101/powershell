using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ProjectAbyss.Client.UI.Screens
{
    public sealed class ClassesScreen : Screen
    {
        public ClassesScreen(AbyssApp app) : base(app)
        {
        }

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            this.Ui.Title("Klassen und Freischaltungen", this.S(90), s);
            int y = this.S(160);
            int x = s.X / 2 - this.S(520);
            foreach (var (id, name) in NewRunScreen.DesignClasses)
            {
                var panel = new Rectangle(x, y, this.S(1040), this.S(70));
                this.Ui.PanelBox(panel);
                string status;
                if (!this.App.Data.Classes.TryGetValue(id, out var cls))
                    status = "In Entwicklung (noch nicht spielbar)";
                else if (this.App.Profile.UnlockedClasses.Contains(id))
                    status = $"Freigeschaltet · {cls.Resource.Name} · Waffen: {string.Join(", ", cls.WeaponTypes.Select(w => this.App.Data.WeaponTypes[w].Name))}";
                else
                    status = "Gesperrt – wird über eine Herausforderung freigeschaltet";
                this.Ui.Label(name, new Vector2(panel.X + this.S(16), panel.Y + this.S(8)), Ui.Accent);
                this.Ui.Label(status, new Vector2(panel.X + this.S(16), panel.Y + this.S(38)), Ui.Muted, scale: 0.85f);
                y += this.S(80);
            }
            var diffs = this.App.Data.Balance.Scaling.Difficulties.Select(d => d.Name + (this.App.Profile.UnlockedDifficulties.Contains(d.Id) ? " ✓" : " (gesperrt)"));
            this.Ui.Label("Schwierigkeitsgrade: " + string.Join("  ·  ", diffs), new Vector2(x, y + this.S(10)));
            this.Back(0, (int)((y + this.S(70)) / this.Ui.Scale));
        }
    }

    public sealed class ArchiveScreen : Screen
    {
        public ArchiveScreen(AbyssApp app) : base(app)
        {
        }

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            this.Ui.Title("Archiv – Bestiarium", this.S(90), s);
            int y = this.S(160);
            int x = s.X / 2 - this.S(400);
            foreach (var e in this.App.Data.Enemies.Values.OrderBy(e => e.Biome).ThenBy(e => e.Name))
            {
                int kills = this.App.Profile.Bestiary.TryGetValue(e.Id, out int k) ? k : 0;
                this.Ui.Label(kills > 0 ? $"{e.Name}  –  Biom {e.Biome}, {RoleName(e.Role)}  –  besiegt: {kills}" : "???  –  noch nicht begegnet", new Vector2(x, y), kills > 0 ? Ui.TextColor : Ui.Muted);
                y += this.S(40);
            }
            foreach (var bo in this.App.Data.Bosses.Values)
            {
                int kills = this.App.Profile.Bestiary.TryGetValue(bo.Id, out int k) ? k : 0;
                this.Ui.Label(kills > 0 ? $"{bo.Name}  –  Boss, Biom {bo.Biome}  –  besiegt: {kills}" : "???  –  Boss", new Vector2(x, y), kills > 0 ? Ui.Accent : Ui.Muted);
                y += this.S(40);
            }
            this.Back(0, 720);
        }

        private static string RoleName(Core.Data.EnemyRole r) => r switch
        {
            Core.Data.EnemyRole.Melee => "Nahkämpfer",
            Core.Data.EnemyRole.Ranged => "Fernkämpfer",
            Core.Data.EnemyRole.Tank => "Tank",
            Core.Data.EnemyRole.Assassin => "Assassine",
            Core.Data.EnemyRole.Caster => "Magier",
            Core.Data.EnemyRole.Support => "Unterstützer",
            _ => "Beschwörer",
        };
    }

    public sealed class StatsScreen : Screen
    {
        public StatsScreen(AbyssApp app) : base(app)
        {
        }

        public override void Render(SpriteBatch b)
        {
            var s = this.ScreenSize;
            var st = this.App.Profile.Statistics;
            this.Ui.Title("Statistiken", this.S(90), s);
            string[] lines =
            {
                $"Begonnene Runs: {st.RunsStarted}",
                $"Gewonnene Runs: {st.RunsWon}",
                $"Verlorene Runs: {st.RunsLost}",
                $"Besiegte Gegner: {st.EnemiesKilled}",
                $"Besiegte Bosse: {st.BossesKilled}",
                $"Tiefste Ebene: {st.DeepestLevel}",
                $"Profil: {this.App.Profile.DisplayName}  ·  Kennung {this.App.Identity.ParticipantId.Substring(0, 11)}…",
            };
            int y = this.S(170);
            foreach (string line in lines)
            {
                this.Ui.Label(line, new Vector2(s.X / 2f - this.S(300), y));
                y += this.S(44);
            }
            this.Back(0, 560);
        }
    }
}
