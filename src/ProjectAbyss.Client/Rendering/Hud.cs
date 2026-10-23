using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Client.UI;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Simulation;

namespace ProjectAbyss.Client.Rendering
{
    /// <summary>Combat HUD: own vitals, resource, lives, ability slots with visible cooldowns, dash charges, party, boss bars.</summary>
    public sealed class Hud
    {
        private readonly AbyssApp app;

        public Hud(AbyssApp app)
        {
            this.app = app;
        }

        private Ui Ui => this.app.Ui;
        private int S(float px) => (int)(px * this.Ui.Scale);

        public void Render(SpriteBatch b, WorldState w, int ownId, string? banner)
        {
            var screen = this.app.Platform.ScreenSize;
            var draw = this.Ui.Draw;
            var data = this.app.Data;
            var me = w.Players.Find(p => p.Id == ownId);
            if (me != null)
            {
                var cls = data.Classes[me.ClassId];
                var panel = new Rectangle(this.S(20), screen.Y - this.S(130), this.S(380), this.S(110));
                this.Ui.PanelBox(panel);
                draw.Bar(b, new Rectangle(panel.X + this.S(14), panel.Y + this.S(14), this.S(350), this.S(22)), me.Hp / me.MaxHp, new Color(170, 30, 40), Color.Black, Color.Gray);
                Draw.Text(b, this.Ui.Small, $"{Math.Max(0, me.Hp):0} / {me.MaxHp:0}", new Vector2(panel.X + this.S(20), panel.Y + this.S(14)), Color.White, this.Ui.Scale * 0.8f);
                draw.Bar(b, new Rectangle(panel.X + this.S(14), panel.Y + this.S(42), this.S(350), this.S(14)), me.Resource / Math.Max(1, me.ResourceMax), new Color(210, 140, 40), Color.Black, Color.Gray);
                Draw.Text(b, this.Ui.Small, $"{cls.Resource.Name} {me.Resource:0}", new Vector2(panel.X + this.S(18), panel.Y + this.S(58)), Ui.Muted, this.Ui.Scale * 0.75f);
                for (int i = 0; i < data.Balance.Lives.StartLives; i++)
                {
                    var r = new Rectangle(panel.X + this.S(14 + i * 26), panel.Y + this.S(82), this.S(20), this.S(18));
                    draw.Rect(b, r, i < me.Lives ? new Color(200, 40, 50) : new Color(50, 40, 50));
                    draw.Frame(b, r, Color.Black, 1);
                }
                Draw.Text(b, this.Ui.Small, $"Leben {me.Lives}   Tränke {me.PotionCharges}", new Vector2(panel.X + this.S(100), panel.Y + this.S(80)), Ui.TextColor, this.Ui.Scale * 0.8f);
                this.DrawSlots(b, w, me, screen);
                if (me.IsProtected(w.Tick))
                    Draw.TextCentered(b, this.Ui.Large, "Wiedereinstieg – geschützt", new Vector2(screen.X / 2f, screen.Y * 0.3f), Color.LightSkyBlue, this.Ui.Scale);
                if (me.LifeState == LifeState.Downed)
                    Draw.TextCentered(b, this.Ui.Large, "Kampfunfähig – warte auf Wiederbelebung", new Vector2(screen.X / 2f, screen.Y * 0.3f), Color.OrangeRed, this.Ui.Scale);
                else if (me.LifeState == LifeState.Respawning)
                    Draw.TextCentered(b, this.Ui.Large, $"Leben verloren – Rückkehr in {Math.Max(0, me.LifeStateUntilTick - w.Tick) / 60f:0.0} s", new Vector2(screen.X / 2f, screen.Y * 0.3f), Color.OrangeRed, this.Ui.Scale);
                else if (me.LifeState == LifeState.Eliminated)
                    Draw.TextCentered(b, this.Ui.Large, "Ausgeschieden – Zuschauer", new Vector2(screen.X / 2f, screen.Y * 0.3f), Color.Gray, this.Ui.Scale);
                var downedAlly = w.Players.FirstOrDefault(p => p.Id != ownId && p.LifeState == LifeState.Downed);
                if (downedAlly != null && me.LifeState == LifeState.Alive)
                {
                    string key = this.app.Profile.Settings.KeyBindings.TryGetValue("interact", out var k) ? k : "G";
                    Draw.TextCentered(b, this.Ui.Small, $"{downedAlly.DisplayName} wiederbeleben: [{key}] in der Nähe halten", new Vector2(screen.X / 2f, screen.Y * 0.36f), Color.LightGreen, this.Ui.Scale);
                }
            }

            // party frames
            int py = this.S(20);
            foreach (var p in w.Players.Where(p => p.Id != ownId))
            {
                var r = new Rectangle(this.S(20), py, this.S(240), this.S(46));
                this.Ui.PanelBox(r);
                string state = !p.Connected ? " (getrennt)" : p.LifeState switch
                {
                    LifeState.Downed => " (kampfunfähig)",
                    LifeState.Eliminated => " (ausgeschieden)",
                    LifeState.Respawning => " (kehrt zurück)",
                    _ => "",
                };
                Draw.Text(b, this.Ui.Small, p.DisplayName + state, new Vector2(r.X + this.S(8), r.Y + this.S(2)), Ui.TextColor, this.Ui.Scale * 0.75f);
                draw.Bar(b, new Rectangle(r.X + this.S(8), r.Y + this.S(28), this.S(224), this.S(10)), p.Hp / p.MaxHp, new Color(170, 30, 40), Color.Black, Color.Gray);
                py += this.S(54);
            }

            // boss bars
            var boss = w.Enemies.FirstOrDefault(e => e.IsBoss && e.Hp > 0);
            if (boss != null && data.Bosses.TryGetValue(boss.DefId, out var bdef))
            {
                var bar = new Rectangle(screen.X / 2 - this.S(400), this.S(36), this.S(800), this.S(24));
                Draw.TextCentered(b, this.Ui.Large, bdef.Name + (w.PactActive ? "  ·  Pakt" : ""), new Vector2(screen.X / 2f, this.S(18)), new Color(230, 200, 160), this.Ui.Scale * 0.8f);
                draw.Bar(b, bar, boss.Hp / boss.MaxHp, new Color(150, 20, 30), Color.Black, new Color(230, 200, 160));
                foreach (var ph in bdef.Phases.Skip(1))
                    draw.Rect(b, new Rectangle(bar.X + (int)(bar.Width * ph.StartsAtHpFraction) - 1, bar.Y, 3, bar.Height), Color.White);
                var st = new Rectangle(bar.X + this.S(200), bar.Bottom + this.S(6), this.S(400), this.S(8));
                bool staggered = boss.Boss != null && w.Tick < boss.Boss.StaggeredUntilTick;
                bool immune = boss.Boss != null && w.Tick < boss.Boss.StaggerImmuneUntilTick && !staggered;
                draw.Bar(b, st, staggered ? 1f : (boss.Boss?.Stagger ?? 0) / bdef.StaggerMax, staggered ? Color.LightSkyBlue : immune ? Color.Gray : new Color(220, 200, 90), Color.Black, Color.Gray);
            }

            // encounter info
            string info = w.Encounter.Kind switch
            {
                EncounterKind.Survival => $"Überleben: {Math.Max(0, w.Encounter.SurvivalEndTick - w.Tick) / 60} s",
                EncounterKind.Boss => "Bosskampf",
                EncounterKind.EliteHunt => "Elitejagd",
                EncounterKind.Training => "Training",
                _ => $"Welle {Math.Max(1, w.Encounter.WaveIndex + 1)} / {Math.Max(1, w.Encounter.Waves.Count)}",
            };
            Draw.Text(b, this.Ui.Small, $"Ebene {w.Level}  ·  {info}", new Vector2(screen.X - this.S(360), this.S(20)), Ui.TextColor, this.Ui.Scale);
            if (banner != null)
                Draw.TextCentered(b, this.Ui.Large, banner, new Vector2(screen.X / 2f, screen.Y * 0.2f), Color.White, this.Ui.Scale);
        }

        private void DrawSlots(SpriteBatch b, WorldState w, PlayerEntity me, Point screen)
        {
            var icons = this.app.Assets.Get("assets/ui/icons.png");
            var meta = this.app.Assets.Meta.Icons;
            var binds = this.app.Profile.Settings.KeyBindings;
            var slots = new (string key, string icon, string cooldown, string bind)[]
            {
                ("special", "special", CombatWorld.SpecialCooldownKey, "special"),
                ("a1", me.EquippedAbilities.ElementAtOrDefault(0) ?? "", me.EquippedAbilities.ElementAtOrDefault(0) ?? "", "ability1"),
                ("a2", me.EquippedAbilities.ElementAtOrDefault(1) ?? "", me.EquippedAbilities.ElementAtOrDefault(1) ?? "", "ability2"),
                ("ult", me.Ultimate, CombatWorld.UltimateCooldownKey, "ultimate"),
                ("potion", "potion", CombatWorld.PotionCooldownKey, "potion"),
            };
            int size = this.S(64);
            int x = screen.X / 2 - (slots.Length * (size + this.S(10))) / 2;
            int y = screen.Y - size - this.S(30);
            foreach (var slot in slots)
            {
                var r = new Rectangle(x, y, size, size);
                this.Ui.PanelBox(r);
                int iconIndex = meta.Order.IndexOf(slot.icon);
                if (iconIndex >= 0)
                    b.Draw(icons, new Rectangle(r.X + this.S(4), r.Y + this.S(4), size - this.S(8), size - this.S(8)), new Rectangle(iconIndex * meta.Size, 0, meta.Size, meta.Size), Color.White);
                if (me.CooldownReadyTick.TryGetValue(slot.cooldown, out int ready) && ready > w.Tick)
                {
                    float total = this.CooldownTotal(slot.cooldown, me);
                    float left = (ready - w.Tick) / 60f;
                    float frac = total > 0 ? Math.Clamp(left / total, 0, 1) : 1;
                    this.Ui.Draw.Rect(b, new Rectangle(r.X, r.Y + (int)(r.Height * (1 - frac)), r.Width, (int)(r.Height * frac)), Color.Black * 0.65f);
                    Draw.TextCentered(b, this.Ui.Small, left >= 1 ? $"{left:0}" : $"{left:0.0}", r.Center.ToVector2(), Color.White, this.Ui.Scale);
                }
                if (slot.key == "ult" && this.app.Data.Abilities.TryGetValue(me.Ultimate, out var ult) && me.Resource < ult.ResourceCost)
                    this.Ui.Draw.Rect(b, r, Color.Black * 0.35f);
                Draw.Text(b, this.Ui.Small, binds.TryGetValue(slot.bind, out var k) ? ShortKey(k) : "?", new Vector2(r.X + this.S(4), r.Bottom - this.S(22)), new Color(230, 210, 120), this.Ui.Scale * 0.7f);
                x += size + this.S(10);
            }
            // dash charges
            int dashMax = this.app.Data.Balance.Dash.Charges;
            for (int i = 0; i < dashMax; i++)
            {
                var r = new Rectangle(screen.X / 2 - dashMax * this.S(14) + i * this.S(28), y - this.S(22), this.S(22), this.S(12));
                this.Ui.Draw.Rect(b, r, i < me.DashCharges ? new Color(130, 200, 250) : new Color(40, 50, 60));
                this.Ui.Draw.Frame(b, r, Color.Black, 1);
            }
        }

        private float CooldownTotal(string key, PlayerEntity me)
        {
            var data = this.app.Data;
            if (key == CombatWorld.SpecialCooldownKey)
                return data.WeaponTypes[me.WeaponTypeId].SpecialCooldownSeconds;
            if (key == CombatWorld.PotionCooldownKey)
                return data.Balance.Potion.CooldownSeconds;
            if (key == CombatWorld.UltimateCooldownKey)
                return data.Abilities.TryGetValue(me.Ultimate, out var u) ? u.CooldownSeconds : 1;
            return data.Abilities.TryGetValue(key, out var a) ? a.CooldownSeconds : 1;
        }

        public static string ShortKey(string key) => key switch
        {
            "MouseLeft" => "LMB",
            "MouseRight" => "RMB",
            "Space" => "Leer",
            _ => key.Length > 4 ? key.Substring(0, 4) : key,
        };
    }
}
