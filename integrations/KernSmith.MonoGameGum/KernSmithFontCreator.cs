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

    /// <summary>
    /// Registers raw font data (TTF/OTF/WOFF) under a family name so that
    /// font generation can resolve it without accessing system fonts.
    /// </summary>
    /// <param name="familyName">Font family name (e.g., "Arial").</param>
    /// <param name="fontData">Raw font file bytes.</param>
    /// <param name="style">
    /// Optional style name (e.g., "Bold", "Italic", "Bold Italic").
    /// When null, registers as the default/regular variant.
    /// </param>
    /// <param name="faceIndex">TTC face index (0 for single-face font files).</param>
    public static void RegisterFont(string familyName, byte[] fontData, string? style = null, int faceIndex = 0)
        => BmFont.RegisterFont(familyName, fontData, style, faceIndex);

    /// <summary>
    /// Removes a previously registered font.
    /// </summary>
    /// <param name="familyName">Font family name.</param>
    /// <param name="style">Optional style name, or null for the default variant.</param>
    /// <returns>True if a font was removed.</returns>
    public static bool UnregisterFont(string familyName, string? style = null)
        => BmFont.UnregisterFont(familyName, style);

    /// <summary>
    /// Removes all registered fonts.
    /// </summary>
    public static void ClearRegisteredFonts()
        => BmFont.ClearRegisteredFonts();

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
