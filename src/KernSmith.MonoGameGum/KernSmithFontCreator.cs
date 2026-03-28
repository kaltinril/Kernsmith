using KernSmith.Atlas;
using KernSmith.Output;
using Microsoft.Xna.Framework.Graphics;
using RenderingLibrary.Graphics;
using RenderingLibrary.Graphics.Fonts;

namespace KernSmith.Gum;

/// <summary>
/// Creates <see cref="BitmapFont"/> instances in memory using KernSmith for Gum games.
/// Generates font textures and metadata without any disk I/O.
/// </summary>
public class KernSmithFontCreator : IInMemoryFontCreator
{
    private readonly GraphicsDevice _graphicsDevice;

    /// <summary>
    /// Initializes a new instance of <see cref="KernSmithFontCreator"/>.
    /// </summary>
    /// <param name="graphicsDevice">
    /// The graphics device used to create font textures.
    /// </param>
    public KernSmithFontCreator(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
    }

    /// <inheritdoc/>
    public BitmapFont? TryCreateFont(BmfcSave bmfcSave)
    {
        BmFontResult result = GumFontGenerator.Generate(bmfcSave);

        string baseName = System.IO.Path.GetFileNameWithoutExtension(bmfcSave.FontCacheFileName);

        Texture2D[] textures = new Texture2D[result.Pages.Count];
        for (int i = 0; i < result.Pages.Count; i++)
        {
            AtlasPage page = result.Pages[i];
            Texture2D texture = new Texture2D(_graphicsDevice, page.Width, page.Height,
                false, SurfaceFormat.Color);
            texture.Name = $"{baseName}_{i}";
            texture.SetData(page.PixelData);
            textures[i] = texture;
        }

        return new BitmapFont(textures, result.FntText);
    }
}
