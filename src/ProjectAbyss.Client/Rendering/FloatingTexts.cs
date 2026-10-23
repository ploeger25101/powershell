using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Core.Mathematics;

namespace ProjectAbyss.Client.Rendering
{
    /// <summary>Damage numbers and short combat callouts drawn above the world.</summary>
    public sealed class FloatingTexts
    {
        private sealed class Entry
        {
            public string Text = "";
            public Vec2 Position;
            public Color Color;
            public int Age;
            public float Scale;
        }

        private const int Lifetime = 50;
        private readonly List<Entry> entries = new();

        public void Add(string text, Vec2 worldPos, Color color, float scale = 1f)
        {
            if (this.entries.Count > 120)
                this.entries.RemoveAt(0);
            this.entries.Add(new Entry { Text = text, Position = worldPos, Color = color, Scale = scale });
        }

        public void Update()
        {
            foreach (var e in this.entries)
                e.Age++;
            this.entries.RemoveAll(e => e.Age > Lifetime);
        }

        public void Render(SpriteBatch b, SpriteFont font, Camera cam)
        {
            foreach (var e in this.entries)
            {
                float t = e.Age / (float)Lifetime;
                var pos = cam.ToScreen(e.Position) + new Vector2(0, -24 - t * 30);
                Draw.TextCentered(b, font, e.Text, pos, e.Color * (1f - t * t), e.Scale);
            }
        }
    }
}
