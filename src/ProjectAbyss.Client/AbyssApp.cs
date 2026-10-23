using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Client.Rendering;
using ProjectAbyss.Client.UI;
using ProjectAbyss.Client.UI.Screens;
using ProjectAbyss.Core.Audio;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Net;
using ProjectAbyss.Core.Persistence;
using ProjectAbyss.Core.Profile;
using ProjectAbyss.Core.Run;

namespace ProjectAbyss.Client
{
    /// <summary>
    /// Root of the Project Abyss presentation: owns the screen stack, profile, identity and audio. Driven by the host
    /// engine through <see cref="Update"/> (fixed 60 Hz) and <see cref="Render"/>.
    /// </summary>
    public sealed class AbyssApp : IDisposable
    {
        public const int DefaultPort = 24642;
        private Screen screen;
        private string? currentMusic;

        public AbyssApp(IAbyssPlatform platform, GameData data)
        {
            this.Platform = platform;
            this.Data = data;
            this.Assets = new AssetLibrary(platform);
            this.Ui = new Ui(new Draw(this.Assets), platform.SmallFont, platform.LargeFont);
            this.WorldRenderer = new WorldRenderer(this.Assets, data);
            this.Audio = new AudioDirector(data);
            string profileDir = Path.Combine(platform.DataDirectory, platform.DeveloperMode ? "dev-profile" : "profile");
            this.ProfileStore = new ProfileStore(profileDir);
            this.ProfileStore.LoadOrCreate(data, Environment.UserName.Length > 0 ? "Wanderer" : "Spieler");
            this.Profile.IsDeveloperProfile = platform.DeveloperMode;
            this.Identity = ParticipantIdentity.LoadOrCreate(Path.Combine(profileDir, "identity.key"));
            this.AssetHash = ComputeAssetHash(platform.ModDirectory);
            this.screen = new MainMenuScreen(this);
        }

        public IAbyssPlatform Platform { get; }
        public GameData Data { get; }
        public AssetLibrary Assets { get; }
        public Ui Ui { get; }
        public WorldRenderer WorldRenderer { get; }
        public AudioDirector Audio { get; }
        public ProfileStore ProfileStore { get; }
        public PlayerProfile Profile => this.ProfileStore.Profile;
        public ParticipantIdentity Identity { get; }
        public string AssetHash { get; }
        public string RunsDirectory => Path.Combine(this.Platform.DataDirectory, this.Platform.DeveloperMode ? "dev-runs" : "runs");
        public Screen Screen => this.screen;

        public void Show(Screen next)
        {
            this.screen.OnLeave();
            this.screen = next;
            this.screen.OnEnter();
        }

        public void Update()
        {
            var size = this.Platform.ScreenSize;
            this.Ui.Scale = Math.Clamp(size.Y / 1080f * this.Profile.Settings.Accessibility.HudScale, 0.75f, 2.5f);
            this.Ui.Capture(this.Platform.MousePosition, this.Platform.IsButtonDown("MouseLeft"));
            this.screen.Update();
            string music = this.screen.DesiredMusic() ?? AudioDirector.MenuMusic;
            if (music != this.currentMusic)
            {
                this.currentMusic = music;
                this.Platform.PlayMusic(music);
            }
        }

        public void Render(SpriteBatch b)
        {
            var size = this.Platform.ScreenSize;
            this.Ui.BeginFrame(b);
            this.Ui.Draw.Rect(b, new Rectangle(0, 0, size.X, size.Y), Ui.Background);
            this.screen.Render(b);
            this.Ui.EndFrame();
        }

        public void PlaySound(SoundRequest r)
        {
            var audio = this.Profile.Settings.Audio;
            this.Platform.PlaySound(r.CueId, r.Volume * audio.Effects * audio.Master);
        }

        public void UiClick() => this.PlaySound(new SoundRequest("abyss_sfx_ui_click", 10, 0.6f));

        /// <summary>Saved single-player/host runs that are still active and contain the local participant.</summary>
        public List<(string dir, RunState run)> FindResumableRuns()
        {
            var list = new List<(string, RunState)>();
            if (!Directory.Exists(this.RunsDirectory))
                return list;
            foreach (string dir in Directory.GetDirectories(this.RunsDirectory))
            {
                var loaded = EnvelopeIO.Load<RunState>(Path.Combine(dir, "run.json"), RunStore.RunKind, RunState.CurrentSchemaVersion);
                var run = loaded.Value;
                if (run != null && run.Status == RunStatus.Active && run.Participants.Any(p => p.ParticipantId == this.Identity.ParticipantId))
                    list.Add((dir, run));
            }
            return list.OrderByDescending(r => r.Item2.CreatedUtc).ToList();
        }

        /// <summary>Hash of every shipped asset file; peers must match (design §25 asset check).</summary>
        private static string ComputeAssetHash(string modDirectory)
        {
            string assets = Path.Combine(modDirectory, "assets");
            if (!Directory.Exists(assets))
                return "no-assets";
            var parts = Directory.GetFiles(assets, "*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => Path.GetRelativePath(assets, f).Replace('\\', '/') + ":" + Hashing.Sha256Hex(File.ReadAllBytes(f)));
            return Hashing.Sha256Hex(string.Join("\n", parts));
        }

        public void Dispose()
        {
            this.screen.OnLeave();
            this.Identity.Dispose();
        }
    }
}
