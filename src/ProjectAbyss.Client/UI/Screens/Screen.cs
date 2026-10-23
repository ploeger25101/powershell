using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ProjectAbyss.Client.UI.Screens
{
    public abstract class Screen
    {
        protected Screen(AbyssApp app)
        {
            this.App = app;
        }

        protected AbyssApp App { get; }
        protected Ui Ui => this.App.Ui;
        protected Point ScreenSize => this.App.Platform.ScreenSize;

        public virtual void OnEnter()
        {
        }

        public virtual void OnLeave()
        {
        }

        public virtual void Update()
        {
        }

        public abstract void Render(SpriteBatch b);

        public virtual string? DesiredMusic() => null;

        protected int S(int px) => (int)(px * this.Ui.Scale);

        protected Rectangle Row(int index, int top, int width = 420, int height = 52) =>
            Ui.Row(this.ScreenSize, index, this.S(top), this.S(width), this.S(height), this.S(10));

        protected bool Back(int index, int top)
        {
            if (this.Ui.Button(this.Row(index, top), "Zurück"))
            {
                this.App.UiClick();
                this.App.Show(new MainMenuScreen(this.App));
                return true;
            }
            return false;
        }
    }
}
