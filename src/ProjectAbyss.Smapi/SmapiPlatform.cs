using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using ProjectAbyss.Client;
using StardewModdingAPI;
using StardewValley;

namespace ProjectAbyss.Smapi
{
    /// <summary>
    /// Bridges the engine-neutral presentation layer to Stardew Valley / SMAPI.
    /// SMAPI calls used here were verified against the SMAPI 4.5 source; Stardew Valley members (Game1.*) could not be
    /// compiled in the development environment and are listed in docs/KNOWN_LIMITATIONS.md for verification.
    /// </summary>
    internal sealed class SmapiPlatform : IAbyssPlatform
    {
        private readonly IModHelper helper;
        private readonly IMonitor monitor;
        private readonly ModConfig config;
        private readonly Queue<string> pressed = new();
        private readonly Dictionary<string, SButton> buttonCache = new();

        public SmapiPlatform(IModHelper helper, IMonitor monitor, ModConfig config, Action openVanilla)
        {
            this.helper = helper;
            this.monitor = monitor;
            this.config = config;
            this.openVanilla = openVanilla;
            // Project Abyss data lives next to (never inside) Stardew's Saves folder.
            this.DataDirectory = Path.Combine(Constants.DataPath, "ProjectAbyss");
            Directory.CreateDirectory(this.DataDirectory);
        }

        private readonly Action openVanilla;

        public string ModDirectory => this.helper.DirectoryPath;
        public string DataDirectory { get; }
        public bool DeveloperMode => this.config.DeveloperMode;

        public SpriteFont SmallFont => Game1.smallFont;
        public SpriteFont LargeFont => Game1.dialogueFont;

        public Point ScreenSize => new(Game1.uiViewport.Width, Game1.uiViewport.Height);

        public Point MousePosition => new(Game1.getMouseX(), Game1.getMouseY());

        public Texture2D? TryLoadTexture(string relativePath)
        {
            try
            {
                return this.helper.ModContent.Load<Texture2D>(relativePath);
            }
            catch (ContentLoadException ex)
            {
                this.monitor.Log($"Could not load '{relativePath}': {ex.Message}", LogLevel.Error);
                return null;
            }
        }

        public Texture2D CreateTexture(int width, int height, Color[] pixels)
        {
            var tex = new Texture2D(Game1.graphics.GraphicsDevice, width, height);
            tex.SetData(pixels);
            return tex;
        }

        public bool IsButtonDown(string buttonName)
        {
            if (!this.buttonCache.TryGetValue(buttonName, out var button))
            {
                if (!Enum.TryParse(buttonName, ignoreCase: true, out button))
                    return false;
                this.buttonCache[buttonName] = button;
            }
            return this.helper.Input.IsDown(button);
        }

        public void OnButtonPressed(SButton button)
        {
            if (this.pressed.Count < 32)
                this.pressed.Enqueue(button.ToString());
        }

        public string? ConsumePressedButton() => this.pressed.Count > 0 ? this.pressed.Dequeue() : null;

        public void ClearPressed() => this.pressed.Clear();

        public void PlaySound(string cueId, float volume)
        {
            if (volume <= 0.01f || !AudioRegistry.IsRegistered(cueId))
                return;
            Game1.playSound(cueId);
        }

        public void PlayMusic(string cueId)
        {
            if (AudioRegistry.IsRegistered(cueId))
                Game1.changeMusicTrack(cueId);
            else
                this.StopMusic();
        }

        public void StopMusic() => Game1.changeMusicTrack("none");

        public void OpenVanillaStardew() => this.openVanilla();

        public void QuitGame() => GameRunner.instance.Exit();

        public void Log(string message, bool error = false) => this.monitor.Log(message, error ? LogLevel.Error : LogLevel.Trace);
    }
}
