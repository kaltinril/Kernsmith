using Bmfontier.Atlas;
using Bmfontier.Font;
using Bmfontier.Rasterizer;

namespace Bmfontier;

/// <summary>
/// Configuration options for BMFont generation.
/// </summary>
public class FontGeneratorOptions
{
    public int Size { get; set; } = 32;
    public CharacterSet Characters { get; set; } = CharacterSet.Ascii;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public AntiAliasMode AntiAlias { get; set; } = AntiAliasMode.Grayscale;

    /// <summary>
    /// Convenience property that sets both <see cref="MaxTextureWidth"/> and <see cref="MaxTextureHeight"/>.
    /// Reading returns <see cref="MaxTextureWidth"/>.
    /// </summary>
    public int MaxTextureSize
    {
        get => MaxTextureWidth;
        set { MaxTextureWidth = value; MaxTextureHeight = value; }
    }

    /// <summary>Maximum atlas texture width in pixels (default 1024).</summary>
    public int MaxTextureWidth { get; set; } = 1024;

    /// <summary>Maximum atlas texture height in pixels (default 1024).</summary>
    public int MaxTextureHeight { get; set; } = 1024;

    public Padding Padding { get; set; } = new Padding(0, 0, 0, 0);
    public Spacing Spacing { get; set; } = new Spacing(1, 1);
    public PackingAlgorithm PackingAlgorithm { get; set; } = PackingAlgorithm.MaxRects;
    public bool Kerning { get; set; } = true;
    public int Outline { get; set; }
    public bool Sdf { get; set; }
    public bool PowerOfTwo { get; set; } = true;
    public int Dpi { get; set; } = 72;
    public int FaceIndex { get; set; }
    public bool ChannelPacking { get; set; }
    public bool ColorFont { get; set; }
    public int ColorPaletteIndex { get; set; }
    public Dictionary<string, float>? VariationAxes { get; set; }

    /// <summary>
    /// Super sampling level (1-4). When greater than 1, glyphs are rasterized
    /// at Nx size then downscaled using a box filter for smoother edges.
    /// </summary>
    public int SuperSampleLevel { get; set; } = 1;

    /// <summary>
    /// Fallback character codepoint to display for missing glyphs.
    /// When set, included in the BMFont output. Common values: '?' (63) or '\uFFFD' (65533).
    /// </summary>
    public char? FallbackCharacter { get; set; }

    /// <summary>
    /// Texture format for atlas output. Default is PNG.
    /// </summary>
    public TextureFormat TextureFormat { get; set; } = TextureFormat.Png;

    /// <summary>
    /// When true, FreeType hinting is enabled for crisp rendering at small sizes.
    /// When false, glyphs are rendered without hinting for smoother curves.
    /// </summary>
    public bool EnableHinting { get; set; } = true;

    /// <summary>
    /// When true, automatically find the smallest power-of-two texture size that fits all glyphs.
    /// Overrides <see cref="MaxTextureWidth"/> and <see cref="MaxTextureHeight"/> with the fitted size.
    /// </summary>
    public bool AutofitTexture { get; set; }

    /// <summary>
    /// When true, all character cells in the atlas are padded to the same height.
    /// YOffset is adjusted so all characters align to a common baseline.
    /// </summary>
    public bool EqualizeCellHeights { get; set; }

    /// <summary>
    /// When true, sets all xoffset and yoffset values to 0 in the output.
    /// Useful for monospace/grid-based text rendering.
    /// </summary>
    public bool ForceOffsetsToZero { get; set; }

    /// <summary>
    /// Per-channel configuration for the atlas texture.
    /// When set, each RGBA channel can independently hold glyph data, outline data,
    /// combined glyph+outline, zero, or one. Produces RGBA output.
    /// </summary>
    public ChannelConfig? Channels { get; set; }

    /// <summary>
    /// Vertical height scaling percentage. 100 = no change. Values above 100 stretch
    /// glyphs taller; values below 100 squish them shorter.
    /// </summary>
    public int HeightPercent { get; set; } = 100;

    /// <summary>
    /// Custom glyph images keyed by codepoint. These replace or add glyphs in the
    /// generated font. Users must supply pre-decoded raw pixel data.
    /// </summary>
    public Dictionary<int, CustomGlyph>? CustomGlyphs { get; set; }

    /// <summary>
    /// When true, adjusts the font size so the tallest rendered character exactly matches
    /// the requested pixel height, rather than using the typographic em size.
    /// </summary>
    public bool MatchCharHeight { get; set; }

    /// <summary>
    /// Hint for the atlas size estimator's expected packing efficiency (0.50 to 0.99).
    /// Default 0.90 is tuned for MaxRects BSSF with font glyphs.
    /// Clamped internally; lower values produce larger atlases with fewer multi-page fallbacks.
    /// </summary>
    internal float PackingEfficiencyHint { get; set; } = 0.90f;

    // Swappable components (null = use defaults)
    public IFontReader? FontReader { get; set; }
    public IRasterizer? Rasterizer { get; set; }
    public IAtlasPacker? Packer { get; set; }
    public IAtlasEncoder? AtlasEncoder { get; set; }
    public IReadOnlyList<IGlyphPostProcessor>? PostProcessors { get; set; }
}
