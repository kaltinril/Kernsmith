using KernSmith.Atlas;
using KernSmith.Font;
using KernSmith.Rasterizer;

namespace KernSmith;

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

    /// <summary>Outline color red channel (default 0 = black).</summary>
    public byte OutlineR { get; set; }

    /// <summary>Outline color green channel (default 0 = black).</summary>
    public byte OutlineG { get; set; }

    /// <summary>Outline color blue channel (default 0 = black).</summary>
    public byte OutlineB { get; set; }
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

    // --- Gradient effect properties ---

    /// <summary>Gradient start (top) color red channel.</summary>
    public byte? GradientStartR { get; set; }

    /// <summary>Gradient start (top) color green channel.</summary>
    public byte? GradientStartG { get; set; }

    /// <summary>Gradient start (top) color blue channel.</summary>
    public byte? GradientStartB { get; set; }

    /// <summary>Gradient end (bottom) color red channel.</summary>
    public byte? GradientEndR { get; set; }

    /// <summary>Gradient end (bottom) color green channel.</summary>
    public byte? GradientEndG { get; set; }

    /// <summary>Gradient end (bottom) color blue channel.</summary>
    public byte? GradientEndB { get; set; }

    /// <summary>Gradient angle in degrees (default 90 = top-to-bottom).</summary>
    public float GradientAngle { get; set; } = 90f;

    /// <summary>Gradient midpoint bias (0.0 to 1.0, default 0.5).</summary>
    public float GradientMidpoint { get; set; } = 0.5f;

    /// <summary>Whether a gradient has been configured.</summary>
    internal bool HasGradient => GradientStartR.HasValue && GradientEndR.HasValue;

    // --- Shadow effect properties ---

    /// <summary>Shadow horizontal offset in pixels (positive = right).</summary>
    public int ShadowOffsetX { get; set; }

    /// <summary>Shadow vertical offset in pixels (positive = down).</summary>
    public int ShadowOffsetY { get; set; }

    /// <summary>Shadow color red channel.</summary>
    public byte ShadowR { get; set; }

    /// <summary>Shadow color green channel.</summary>
    public byte ShadowG { get; set; }

    /// <summary>Shadow color blue channel.</summary>
    public byte ShadowB { get; set; }

    /// <summary>Shadow opacity (0.0 to 1.0, default 1.0).</summary>
    public float ShadowOpacity { get; set; } = 1.0f;

    /// <summary>Shadow blur radius. 0 = hard shadow.</summary>
    public int ShadowBlur { get; set; }

    /// <summary>Whether a shadow has been configured.</summary>
    internal bool HasShadow => ShadowOffsetX != 0 || ShadowOffsetY != 0 || ShadowBlur > 0;

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

    /// <summary>
    /// When true, collects timing data for each pipeline stage.
    /// Results are available via <see cref="BmFontResult.Metrics"/>.
    /// </summary>
    public bool CollectMetrics { get; set; }
}
