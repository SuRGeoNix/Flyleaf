using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PartyTime.UI_Example
{
    public class frmDisplay : Game
    {
        GraphicsDeviceManager   graphics;
        SpriteBatch             spriteBatch;
        UserInterface           UI;

        public frmDisplay()
        {
            graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
        }
        protected override void Initialize()                { base.Initialize(); }
        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
            UI = new UserInterface(this, ref graphics, ref spriteBatch);
        }
        protected override void UnloadContent()             { }
        protected override void Update(GameTime gameTime)   { UI.UpdateLoop(); SuppressDraw(); base.Update(gameTime); }
        protected override bool BeginDraw()                 { return false; }
    }
}
