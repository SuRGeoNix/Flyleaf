using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PartyTime.UI_Example
{
    public class frmDisplay : Game
    {
        GraphicsDeviceManager   graphics;
        SpriteBatch             spriteBatch;
        UserInterface           UI;
        
        public                  frmDisplay()                { graphics = new GraphicsDeviceManager(this); graphics.GraphicsProfile = GraphicsProfile.HiDef; Content.RootDirectory = "Content"; }
        protected override void Initialize()                { base.Initialize(); this.TargetElapsedTime = System.TimeSpan.FromSeconds(1.0f / 2.0f); /*this.IsFixedTimeStep = true; this.TargetElapsedTime = System.TimeSpan.FromSeconds(1.0f / 5.0f);*/ }
        protected override void LoadContent()               { spriteBatch = new SpriteBatch(GraphicsDevice); UI = new UserInterface(this, ref graphics, ref spriteBatch); }
        protected override void UnloadContent()             { }
        protected override void Update(GameTime gameTime)   { UI.UpdateLoop(); SuppressDraw(); base.Update(gameTime); }
        protected override bool BeginDraw()                 { return false; }
    }
}