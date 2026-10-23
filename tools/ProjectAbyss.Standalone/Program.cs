using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ProjectAbyss.Client;
using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Standalone
{
    /// <summary>
    /// Usage: ProjectAbyss.Standalone [--script file.json] [--out dir] [--size 1600x900] [--data dir] [--dev]
    /// Script steps: {"frame":N, "click":"Button label"} | {"frame":N,"down":"W"} | {"frame":N,"up":"W"} |
    /// {"frame":N,"mouse":[x,y]} | {"frame":N,"screenshot":"name"} | {"frame":N,"quit":true}
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var opts = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--"))
                    opts[args[i].Substring(2)] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
            }
            using var game = new StandaloneGame(opts);
            game.Run();
            return game.ExitCode;
        }
    }

    public sealed class ScriptStep
    {
        public int Frame { get; set; }
        public string? Click { get; set; }
        public string? Down { get; set; }
        public string? Up { get; set; }
        public int[]? Mouse { get; set; }
        public string? Screenshot { get; set; }
        public bool Quit { get; set; }
        public string? Log { get; set; }
    }

    public sealed class StandaloneGame : Game, IAbyssPlatform
    {
        private readonly GraphicsDeviceManager graphics;
        private readonly Dictionary<string, string> opts;
        private readonly List<ScriptStep> script = new();
        private readonly HashSet<string> scriptedDown = new();
        private readonly Queue<string> pressed = new();
        private readonly string outDir;
        private SpriteBatch? batch;
        private RenderTarget2D? target;
        private AbyssApp? app;
        private int frame;
        private Point scriptedMouse = new(-100, -100);
        private bool scripted;
        private int pendingClickFrames;
        private readonly Stopwatch fpsWatch = Stopwatch.StartNew();
        private int fpsFrames;
        private readonly List<double> updateMs = new();
        private readonly List<double> drawMs = new();

        public int ExitCode { get; private set; }

        public StandaloneGame(Dictionary<string, string> opts)
        {
            this.opts = opts;
            this.graphics = new GraphicsDeviceManager(this);
            var size = (opts.TryGetValue("size", out var s) ? s : "1600x900").Split('x').Select(int.Parse).ToArray();
            this.graphics.PreferredBackBufferWidth = size[0];
            this.graphics.PreferredBackBufferHeight = size[1];
            // scripted runs need exactly one Draw per Update so clicks hit the buttons of the current frame
            this.IsFixedTimeStep = !opts.ContainsKey("uncapped") && !opts.ContainsKey("script");
            this.graphics.SynchronizeWithVerticalRetrace = false;
            this.TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 60.0);
            this.IsMouseVisible = true;
            this.outDir = opts.TryGetValue("out", out var o) ? o : Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(this.outDir);
            if (opts.TryGetValue("script", out var scriptPath))
            {
                this.scripted = true;
                this.script = JsonSerializer.Deserialize<List<ScriptStep>>(File.ReadAllText(scriptPath), GameData.JsonOptions) ?? new();
            }
            this.DataDirectory = opts.TryGetValue("data", out var d) ? d : Path.Combine(Path.GetTempPath(), "abyss-standalone");
            Directory.CreateDirectory(this.DataDirectory);
        }

        // ------------------------------------------------------------ IAbyssPlatform

        public SpriteFont SmallFont { get; private set; } = null!;
        public SpriteFont LargeFont { get; private set; } = null!;
        public Point ScreenSize => new(this.graphics.PreferredBackBufferWidth, this.graphics.PreferredBackBufferHeight);
        public Point MousePosition => this.scripted ? this.scriptedMouse : Mouse.GetState().Position;
        public string ModDirectory => AppContext.BaseDirectory;
        public string DataDirectory { get; }
        public bool DeveloperMode => this.opts.ContainsKey("dev");

        public Texture2D? TryLoadTexture(string relativePath)
        {
            string path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path))
                return null;
            using var fs = File.OpenRead(path);
            return Texture2D.FromStream(this.GraphicsDevice, fs);
        }

        public Texture2D CreateTexture(int width, int height, Color[] pixels)
        {
            var t = new Texture2D(this.GraphicsDevice, width, height);
            t.SetData(pixels);
            return t;
        }

        public bool IsButtonDown(string buttonName)
        {
            if (this.scripted)
                return this.scriptedDown.Contains(buttonName);
            var mouse = Mouse.GetState();
            if (buttonName == "MouseLeft")
                return mouse.LeftButton == ButtonState.Pressed;
            if (buttonName == "MouseRight")
                return mouse.RightButton == ButtonState.Pressed;
            return Enum.TryParse<Keys>(buttonName, out var key) && Keyboard.GetState().IsKeyDown(key);
        }

        public string? ConsumePressedButton() => this.pressed.Count > 0 ? this.pressed.Dequeue() : null;
        public void PlaySound(string cueId, float volume) { }
        public void PlayMusic(string cueId) => Console.WriteLine($"[music] {cueId}");
        public void StopMusic() { }
        public void OpenVanillaStardew() => Console.WriteLine("[platform] vanilla Stardew requested (not available standalone)");
        public void QuitGame() => this.Exit();
        public void Log(string message, bool error = false) => Console.WriteLine((error ? "[error] " : "[log] ") + message);

        // ------------------------------------------------------------ MonoGame

        protected override void LoadContent()
        {
            this.batch = new SpriteBatch(this.GraphicsDevice);
            this.target = new RenderTarget2D(this.GraphicsDevice, this.ScreenSize.X, this.ScreenSize.Y);
            this.SmallFont = this.LoadFont("font_small");
            this.LargeFont = this.LoadFont("font_large");
            var data = GameData.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
            this.app = new AbyssApp(this, data);
        }

        private SpriteFont LoadFont(string name)
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "fonts");
            var tex = this.TryLoadTexture(Path.Combine("fonts", name + ".png"))!;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, name + ".json")));
            var glyphs = new List<Rectangle>();
            var crop = new List<Rectangle>();
            var chars = new List<char>();
            var kerning = new List<Vector3>();
            foreach (var g in doc.RootElement.GetProperty("glyphs").EnumerateArray())
            {
                int w = g.GetProperty("w").GetInt32();
                int h = g.GetProperty("h").GetInt32();
                glyphs.Add(new Rectangle(g.GetProperty("x").GetInt32(), g.GetProperty("y").GetInt32(), w, h));
                crop.Add(new Rectangle(g.GetProperty("ox").GetInt32(), g.GetProperty("oy").GetInt32(), w, h));
                chars.Add((char)g.GetProperty("c").GetInt32());
                float adv = (float)g.GetProperty("adv").GetDouble();
                kerning.Add(new Vector3(0, adv, 0));
            }
            // no default character on purpose: exercises the glyph fallback the game fonts may need
            return new SpriteFont(tex, glyphs, crop, chars, doc.RootElement.GetProperty("lineSpacing").GetInt32(), 0f, kerning, null);
        }

        protected override void Update(GameTime gameTime)
        {
            this.frame++;
            if (this.scripted)
                this.RunScript();
            var sw = Stopwatch.StartNew();
            this.app!.Update();
            this.updateMs.Add(sw.Elapsed.TotalMilliseconds);
            if (this.pendingClickFrames > 0 && --this.pendingClickFrames == 0)
                this.scriptedDown.Remove("MouseLeft");
            base.Update(gameTime);
        }

        private void RunScript()
        {
            foreach (var step in this.script.Where(s => s.Frame == this.frame))
            {
                if (step.Log != null)
                    Console.WriteLine($"[script] {step.Log}");
                if (step.Mouse != null)
                    this.scriptedMouse = new Point(step.Mouse[0], step.Mouse[1]);
                if (step.Click != null)
                {
                    var button = this.app!.Ui.LastButtons.FirstOrDefault(b => b.Label.StartsWith(step.Click, StringComparison.Ordinal));
                    if (button.Label == null)
                    {
                        Console.WriteLine($"[script] frame {this.frame}: button '{step.Click}' not found. Visible: {string.Join(" | ", this.app.Ui.LastButtons.Select(b => b.Label))}");
                        this.ExitCode = 3;
                    }
                    else
                    {
                        this.scriptedMouse = button.Bounds.Center;
                        this.scriptedDown.Add("MouseLeft");
                        this.pendingClickFrames = 2;
                    }
                }
                if (step.Down != null)
                {
                    this.scriptedDown.Add(step.Down);
                    this.pressed.Enqueue(step.Down);
                }
                if (step.Up != null)
                    this.scriptedDown.Remove(step.Up);
                if (step.Screenshot != null)
                    this.screenshotPending = step.Screenshot;
                if (step.Quit)
                {
                    this.Report();
                    this.Exit();
                }
            }
        }

        private string? screenshotPending;

        protected override void Draw(GameTime gameTime)
        {
            var sw = Stopwatch.StartNew();
            this.GraphicsDevice.SetRenderTarget(this.target);
            this.GraphicsDevice.Clear(Color.Black);
            this.batch!.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            this.app!.Render(this.batch);
            this.batch.End();
            this.GraphicsDevice.SetRenderTarget(null);
            this.batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
            this.batch.Draw(this.target, Vector2.Zero, Color.White);
            this.batch.End();
            this.drawMs.Add(sw.Elapsed.TotalMilliseconds);
            if (this.screenshotPending != null)
            {
                string path = Path.Combine(this.outDir, this.screenshotPending + ".png");
                using (var fs = File.Create(path))
                    this.target!.SaveAsPng(fs, this.target.Width, this.target.Height);
                Console.WriteLine($"[screenshot] {path}");
                this.screenshotPending = null;
            }
            this.fpsFrames++;
            base.Draw(gameTime);
        }

        private void Report()
        {
            double secs = this.fpsWatch.Elapsed.TotalSeconds;
            var u = this.updateMs.Skip(30).OrderBy(x => x).ToList();
            var d = this.drawMs.Skip(30).OrderBy(x => x).ToList();
            if (u.Count == 0 || d.Count == 0)
                return;
            Console.WriteLine($"[perf] frames={this.fpsFrames} wall={secs:0.0}s avgFps={this.fpsFrames / secs:0.0} update avg={u.Average():0.00}ms p99={u[(int)(u.Count * 0.99)]:0.00}ms draw avg={d.Average():0.00}ms p99={d[(int)(d.Count * 0.99)]:0.00}ms");
        }
    }
}
