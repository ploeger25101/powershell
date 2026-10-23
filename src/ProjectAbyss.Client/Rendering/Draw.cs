using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ProjectAbyss.Client.Rendering
{
    /// <summary>Primitive drawing helpers on top of SpriteBatch (pixel texture + circle texture).</summary>
    public sealed class Draw
    {
        private readonly AssetLibrary assets;

        public Draw(AssetLibrary assets)
        {
            this.assets = assets;
        }

        public void Rect(SpriteBatch b, Rectangle r, Color c) => b.Draw(this.assets.Pixel, r, c);

        public void Frame(SpriteBatch b, Rectangle r, Color c, int t = 2)
        {
            this.Rect(b, new Rectangle(r.X, r.Y, r.Width, t), c);
            this.Rect(b, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
            this.Rect(b, new Rectangle(r.X, r.Y, t, r.Height), c);
            this.Rect(b, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
        }

        public void Line(SpriteBatch b, Vector2 a, Vector2 z, Color c, float thickness)
        {
            var d = z - a;
            float len = d.Length();
            if (len < 0.5f)
                return;
            b.Draw(this.assets.Pixel, a, null, c, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(len, thickness), SpriteEffects.None, 0f);
        }

        public void Circle(SpriteBatch b, Vector2 center, float radius, Color c)
        {
            float size = radius * 2f;
            b.Draw(this.assets.Circle, new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)size, (int)size), c);
        }

        public void Ring(SpriteBatch b, Vector2 center, float radius, Color c, float thickness, float startAngle = 0f, float sweep = MathF.PI * 2f, int segments = 40)
        {
            int n = Math.Max(3, (int)(segments * sweep / (MathF.PI * 2f)));
            var prev = center + new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle)) * radius;
            for (int i = 1; i <= n; i++)
            {
                float a = startAngle + sweep * i / n;
                var p = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                this.Line(b, prev, p, c, thickness);
                prev = p;
            }
        }

        /// <summary>Filled cone as a fan of thin lines (screen-space angles, +Y down).</summary>
        public void Sector(SpriteBatch b, Vector2 origin, float screenAngle, float halfAngle, float radius, Color fill, Color edge)
        {
            int rays = Math.Max(6, (int)(halfAngle * 2 * radius / 3f));
            for (int i = 0; i <= rays; i++)
            {
                float a = screenAngle - halfAngle + 2 * halfAngle * i / rays;
                this.Line(b, origin, origin + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, fill, 3f);
            }
            this.Ring(b, origin, radius, edge, 2f, screenAngle - halfAngle, halfAngle * 2);
            this.Line(b, origin, origin + new Vector2(MathF.Cos(screenAngle - halfAngle), MathF.Sin(screenAngle - halfAngle)) * radius, edge, 2f);
            this.Line(b, origin, origin + new Vector2(MathF.Cos(screenAngle + halfAngle), MathF.Sin(screenAngle + halfAngle)) * radius, edge, 2f);
        }

        public void Bar(SpriteBatch b, Rectangle r, float fraction, Color fill, Color back, Color border)
        {
            this.Rect(b, r, back);
            fraction = Math.Clamp(fraction, 0f, 1f);
            this.Rect(b, new Rectangle(r.X, r.Y, (int)(r.Width * fraction), r.Height), fill);
            this.Frame(b, r, border, 1);
        }

        private static readonly System.Collections.Generic.Dictionary<SpriteFont, System.Collections.Generic.HashSet<char>> Glyphs = new();

        /// <summary>
        /// Replace characters the font cannot draw (SpriteFont throws on unknown glyphs unless it has a default character),
        /// so text works with whatever fonts the host game provides.
        /// </summary>
        public static string Safe(SpriteFont font, string text)
        {
            if (font.DefaultCharacter != null)
                return text;
            if (!Glyphs.TryGetValue(font, out var set))
                Glyphs[font] = set = new System.Collections.Generic.HashSet<char>(font.Characters);
            System.Text.StringBuilder? sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (set.Contains(c) || c == '\n')
                {
                    sb?.Append(c);
                    continue;
                }
                sb ??= new System.Text.StringBuilder(text.Substring(0, i));
                string repl = c switch
                {
                    '–' or '—' or '−' or '·' => "-",
                    '…' => "...",
                    '✓' => "+",
                    '◄' => "<",
                    'ä' => "ae",
                    'ö' => "oe",
                    'ü' => "ue",
                    'Ä' => "Ae",
                    'Ö' => "Oe",
                    'Ü' => "Ue",
                    'ß' => "ss",
                    _ => "?",
                };
                foreach (char r in repl)
                    sb.Append(set.Contains(r) ? r : ' ');
            }
            return sb?.ToString() ?? text;
        }

        public static void Text(SpriteBatch b, SpriteFont font, string text, Vector2 pos, Color c, float scale = 1f, bool shadow = true)
        {
            text = Safe(font, text);
            if (shadow)
                b.DrawString(font, text, pos + new Vector2(2, 2) * scale, Color.Black * 0.8f, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            b.DrawString(font, text, pos, c, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        public static void TextCentered(SpriteBatch b, SpriteFont font, string text, Vector2 center, Color c, float scale = 1f)
        {
            text = Safe(font, text);
            var size = font.MeasureString(text) * scale;
            Text(b, font, text, center - size / 2f, c, scale);
        }
    }
}
