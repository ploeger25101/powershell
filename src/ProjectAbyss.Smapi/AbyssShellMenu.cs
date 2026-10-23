using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ProjectAbyss.Client;
using StardewValley;
using StardewValley.Menus;

namespace ProjectAbyss.Smapi
{
    /// <summary>
    /// Full-screen menu that hosts the whole Project Abyss application on Stardew's title screen. No save file, farm or
    /// world is loaded while it is open. It swallows clicks and keys so the vanilla title logic never reacts.
    /// </summary>
    internal sealed class AbyssShellMenu : IClickableMenu
    {
        private readonly AbyssApp app;

        public AbyssShellMenu(AbyssApp app)
            : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height, showUpperRightCloseButton: false)
        {
            this.app = app;
        }

        public AbyssApp App => this.app;

        public override void draw(SpriteBatch b) => this.app.Render(b);

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
        }

        public override void receiveRightClick(int x, int y, bool playSound = true)
        {
        }

        public override void receiveKeyPress(Keys key)
        {
        }

        public override void receiveScrollWheelAction(int direction)
        {
        }

        public override bool readyToClose() => false;

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            this.width = Game1.uiViewport.Width;
            this.height = Game1.uiViewport.Height;
        }
    }
}
