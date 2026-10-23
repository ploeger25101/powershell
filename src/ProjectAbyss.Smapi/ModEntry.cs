using System;
using System.IO;
using Microsoft.Xna.Framework;
using ProjectAbyss.Client;
using ProjectAbyss.Core.Data;
using ProjectAbyss.Core.Persistence;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace ProjectAbyss.Smapi
{
    /// <summary>
    /// SMAPI entry point. Replaces the title screen with the Project Abyss shell, keeps vanilla Stardew reachable, and
    /// drives the game at Stardew's fixed update rate. Never touches Stardew save files.
    /// </summary>
    public sealed class ModEntry : Mod
    {
        private ModConfig config = new();
        private GameData? data;
        private SmapiPlatform? platform;
        private AbyssApp? app;
        /// <summary>True while the player chose vanilla Stardew; the next title screen shows the vanilla menu.</summary>
        private bool vanillaMode;
        private Rectangle returnButton;

        public override void Entry(IModHelper helper)
        {
            this.config = helper.ReadConfig<ModConfig>();
            this.vanillaMode = !this.config.ShowAbyssOnStartup;
            EnvelopeIO.GameVersion = this.ModManifest.Version.ToString();
            try
            {
                this.data = GameData.LoadFromDirectory(Path.Combine(helper.DirectoryPath, "data"));
            }
            catch (GameDataException ex)
            {
                // without valid data Project Abyss stays disabled; vanilla Stardew is unaffected
                this.Monitor.Log("Project Abyss is disabled because its game data is invalid:\n" + ex.Message, LogLevel.Error);
                return;
            }
            AudioRegistry.Scan(helper.DirectoryPath);
            helper.Events.Content.AssetRequested += (_, e) => AudioRegistry.OnAssetRequested(e, helper.DirectoryPath);
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.vanillaMode = !this.config.ShowAbyssOnStartup;
        }

        private AbyssApp GetApp()
        {
            if (this.app == null)
            {
                this.platform = new SmapiPlatform(this.Helper, this.Monitor, this.config, this.OpenVanilla);
                this.app = new AbyssApp(this.platform, this.data!);
                this.Monitor.Log($"Project Abyss ready (content {this.data!.ContentHash.Substring(0, 12)}, data in {this.platform.DataDirectory}).", LogLevel.Info);
            }
            return this.app;
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // Only the title screen is replaced; once a Stardew save is loaded nothing of Project Abyss is active.
            if (e.NewMenu is TitleMenu && !this.vanillaMode && !Context.IsWorldReady)
                Game1.activeClickableMenu = new AbyssShellMenu(this.GetApp());
        }

        private void OpenVanilla()
        {
            this.vanillaMode = true;
            this.platform?.StopMusic();
            Game1.activeClickableMenu = new TitleMenu();
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (Game1.activeClickableMenu is AbyssShellMenu shell)
            {
                try
                {
                    shell.App.Update();
                }
                catch (Exception ex)
                {
                    // never take the whole game down; log and fall back to the Abyss main menu
                    this.Monitor.Log("Project Abyss error: " + ex, LogLevel.Error);
                    shell.App.Show(new Client.UI.Screens.MainMenuScreen(shell.App));
                }
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (Game1.activeClickableMenu is AbyssShellMenu)
            {
                this.platform?.OnButtonPressed(e.Button);
                return;
            }
            // "back to Project Abyss" button drawn on the vanilla title screen
            if (e.Button == SButton.MouseLeft && Game1.activeClickableMenu is TitleMenu && TitleMenu.subMenu == null && this.returnButton.Contains(Game1.getMouseX(), Game1.getMouseY()))
            {
                this.vanillaMode = false;
                this.Helper.Input.Suppress(SButton.MouseLeft);
                Game1.activeClickableMenu = new AbyssShellMenu(this.GetApp());
            }
        }

        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            if (Game1.activeClickableMenu is not TitleMenu || TitleMenu.subMenu != null || this.data == null)
                return;
            var font = Game1.smallFont;
            const string label = "< Project Abyss";
            var size = font.MeasureString(label);
            this.returnButton = new Rectangle(16, 16, (int)size.X + 24, (int)size.Y + 16);
            var b = e.SpriteBatch;
            b.Draw(Game1.staminaRect, this.returnButton, new Color(24, 20, 32) * 0.9f);
            b.DrawString(font, label, new Vector2(this.returnButton.X + 12, this.returnButton.Y + 8), new Color(226, 218, 232));
        }
    }
}
