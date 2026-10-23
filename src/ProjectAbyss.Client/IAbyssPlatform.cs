using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ProjectAbyss.Client
{
    /// <summary>
    /// Everything the presentation layer needs from the host engine. Implemented by the SMAPI mod; keeps this project
    /// free of Stardew Valley references so it can be compiled and reviewed against plain MonoGame.
    /// </summary>
    public interface IAbyssPlatform
    {
        /// <summary>Load a texture from the mod folder; null (and a logged error) if it is missing or unreadable.</summary>
        Texture2D? TryLoadTexture(string relativePath);
        Texture2D CreateTexture(int width, int height, Color[] pixels);
        SpriteFont SmallFont { get; }
        SpriteFont LargeFont { get; }
        Point ScreenSize { get; }
        Point MousePosition { get; }
        /// <summary>Whether the button bound to this name (SMAPI SButton name, e.g. "Q", "MouseLeft") is held.</summary>
        bool IsButtonDown(string buttonName);
        /// <summary>Name of a button pressed this frame (for key rebinding), or null.</summary>
        string? ConsumePressedButton();
        string ModDirectory { get; }
        /// <summary>Folder for Project Abyss profiles and runs (outside Stardew's save folder).</summary>
        string DataDirectory { get; }
        bool DeveloperMode { get; }
        void PlaySound(string cueId, float volume);
        void PlayMusic(string cueId);
        void StopMusic();
        void OpenVanillaStardew();
        void QuitGame();
        void Log(string message, bool error = false);
    }
}
