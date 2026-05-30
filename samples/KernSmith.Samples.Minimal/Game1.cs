using KernSmith;
using KernSmith.Atlas;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace KernSmith.Samples.Minimal;

public class Game1 : Game
{
    private GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _atlas = null!;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this);
        IsMouseVisible = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        // Load a TTF and generate a bitmap font — that's it
        var fontPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tests", "KernSmith.Tests", "Fixtures", "Roboto-Regular.ttf"));

        var result = BmFont.Generate(File.ReadAllBytes(fontPath), new FontGeneratorOptions
        {
            Size = 32,
            Backend = RasterizerBackend.StbTrueType
        });

        // Load the atlas texture into MonoGame.
        // GetRgbaPixelData() returns RGBA bytes regardless of the page's native
        // format (grayscale pages expand to white-with-alpha automatically).
        AtlasPage page = result.Pages[0];
        _atlas = new Texture2D(GraphicsDevice, page.Width, page.Height, false, SurfaceFormat.Color);
        _atlas.SetData(page.GetRgbaPixelData());
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        // Draw the generated atlas texture
        _spriteBatch.Begin(blendState: BlendState.AlphaBlend);
        _spriteBatch.Draw(_atlas, Vector2.Zero, Color.White);
        _spriteBatch.End();

        base.Draw(gameTime);
    }
}
