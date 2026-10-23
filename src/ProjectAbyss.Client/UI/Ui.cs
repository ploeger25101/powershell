using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Client.Rendering;

namespace ProjectAbyss.Client.UI
{
    /// <summary>
    /// Minimal immediate-mode UI: widgets are declared every frame during Render and report clicks from the input
    /// captured in Update. Keeps menus functional without a retained widget tree.
    /// </summary>
    public sealed class Ui
    {
        public static readonly Color Background = new(10, 8, 14);
        public static readonly Color Panel = new(24, 20, 32);
        public static readonly Color Border = new(90, 70, 110);
        public static readonly Color Accent = new(190, 40, 56);
        public static readonly Color TextColor = new(226, 218, 232);
        public static readonly Color Muted = new(130, 120, 140);

        private readonly Draw draw;
        private bool mouseDown;
        private bool clickedThisFrame;
        private bool consumed;

        public Ui(Draw draw, SpriteFont small, SpriteFont large)
        {
            this.draw = draw;
            this.Small = small;
            this.Large = large;
        }

        public SpriteFont Small { get; }
        public SpriteFont Large { get; }
        public Point Mouse { get; private set; }
        public Draw Draw => this.draw;
        public float Scale { get; set; } = 1f;
        public SpriteBatch Batch { get; set; } = null!;

        /// <summary>Buttons drawn in the last frame (label, bounds, enabled); used by automated UI tests.</summary>
        public System.Collections.Generic.List<(string Label, Rectangle Bounds, bool Enabled)> LastButtons { get; } = new();
        private readonly System.Collections.Generic.List<(string, Rectangle, bool)> currentButtons = new();

        /// <summary>Called once per update with the raw mouse state.</summary>
        public void Capture(Point mouse, bool leftDown)
        {
            this.Mouse = mouse;
            if (leftDown && !this.mouseDown)
                this.clickedThisFrame = true;
            this.mouseDown = leftDown;
        }

        /// <summary>Called at the start of each render pass.</summary>
        public void BeginFrame(SpriteBatch b)
        {
            this.Batch = b;
            this.consumed = false;
            this.currentButtons.Clear();
        }

        /// <summary>Called after render: a click not consumed by any widget is dropped.</summary>
        public void EndFrame()
        {
            this.clickedThisFrame = false;
            this.LastButtons.Clear();
            this.LastButtons.AddRange(this.currentButtons);
        }

        public bool Hover(Rectangle r) => r.Contains(this.Mouse);

        public bool Button(Rectangle r, string label, bool enabled = true, bool selected = false)
        {
            this.currentButtons.Add((label, r, enabled));
            bool hover = enabled && this.Hover(r);
            this.draw.Rect(this.Batch, r, selected ? new Color(70, 26, 40) : hover ? new Color(46, 36, 58) : Panel);
            this.draw.Frame(this.Batch, r, selected ? Accent : hover ? new Color(170, 140, 200) : Border, 2);
            var size = this.Small.MeasureString(Rendering.Draw.Safe(this.Small, label)) * this.Scale;
            Rendering.Draw.Text(this.Batch, this.Small, label, new Vector2(r.Center.X - size.X / 2, r.Center.Y - size.Y / 2), enabled ? TextColor : Muted, this.Scale);
            if (hover && this.clickedThisFrame && !this.consumed)
            {
                this.consumed = true;
                return true;
            }
            return false;
        }

        public void Label(string text, Vector2 pos, Color? color = null, bool large = false, float scale = 1f) =>
            Rendering.Draw.Text(this.Batch, large ? this.Large : this.Small, text, pos, color ?? TextColor, scale * this.Scale);

        public void Title(string text, int y, Point screen) =>
            Rendering.Draw.TextCentered(this.Batch, this.Large, text, new Vector2(screen.X / 2f, y), Accent, 1.5f * this.Scale);

        public void PanelBox(Rectangle r)
        {
            this.draw.Rect(this.Batch, r, Panel * 0.95f);
            this.draw.Frame(this.Batch, r, Border, 2);
        }

        /// <summary>Word-wrapped paragraph; returns the height used.</summary>
        public int Paragraph(string text, Rectangle area, Color? color = null)
        {
            float lineH = this.Small.LineSpacing * this.Scale;
            float y = area.Y;
            foreach (string para in text.Split('\n'))
            {
                string line = "";
                foreach (string word in para.Split(' '))
                {
                    string test = line.Length == 0 ? word : line + " " + word;
                    if (this.Small.MeasureString(Rendering.Draw.Safe(this.Small, test)).X * this.Scale > area.Width && line.Length > 0)
                    {
                        this.Label(line, new Vector2(area.X, y), color);
                        y += lineH;
                        line = word;
                    }
                    else
                    {
                        line = test;
                    }
                }
                this.Label(line, new Vector2(area.X, y), color);
                y += lineH;
            }
            return (int)Math.Ceiling(y - area.Y);
        }

        /// <summary>Vertical stack of equally sized buttons centred on screen.</summary>
        public static Rectangle Row(Point screen, int index, int top, int width = 420, int height = 52, int gap = 10) =>
            new(screen.X / 2 - width / 2, top + index * (height + gap), width, height);
    }
}
