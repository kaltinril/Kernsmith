using KernSmith.Atlas;
using KernSmith.Font;
using KernSmith.Output;
using KernSmith.Rasterizer;

namespace KernSmith;

/// <summary>
/// All the settings for generating a BMFont. Sensible defaults are provided.
/// </summary>
public class FontGeneratorOptions
{
    /// <summary>Font size in pixels (default 32).</summary>
    public int Size { get; set; } = 32;

    /// <summary>Character set to include in the generated font.</summary>
    public CharacterSet Characters { get; set; } = CharacterSet.Ascii;

    /// <summary>If true, applies synthetic bold.</summary>
    public bool Bold { get; set; }

    /// <summary>If true, applies synthetic italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Anti-aliasing mode. Default is grayscale.</summary>
    public AntiAliasMode AntiAlias { get; set; } = AntiAliasMode.Grayscale;

    /// <summary>
    /// Shortcut to set both MaxTextureWidth and MaxTextureHeight at once.
    /// Reading returns MaxTextureWidth.
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

    /// <summary>Padding around each glyph in the atlas.</summary>
    public Padding Padding { get; set; } = new Padding(0, 0, 0, 0);

    /// <summary>Spacing between glyphs in the atlas.</summary>
    public Spacing Spacing { get; set; } = new Spacing(1, 1);

    /// <summary>Algorithm used for packing glyphs into atlas pages.</summary>
    public PackingAlgorithm PackingAlgorithm { get; set; } = PackingAlgorithm.MaxRects;

    /// <summary>If true, includes kerning pairs in the output (default true).</summary>
    public bool Kerning { get; set; } = true;

    /// <summary>Outline thickness in pixels. 0 = no outline.</summary>
    public int Outline { get; set; }

    /// <summary>Outline color red channel (default 0 = black).</summary>
    public byte OutlineR { get; set; }

    /// <summary>Outline color green channel (default 0 = black).</summary>
    public byte OutlineG { get; set; }

    /// <summary>Outline color blue channel (default 0 = black).</summary>
    public byte OutlineB { get; set; }

    /// <summary>If true, generates a signed distance field (SDF) font.</summary>
    public bool Sdf { get; set; }

    /// <summary>If true, atlas dimensions are rounded up to powers of two (default true).</summary>
    public bool PowerOfTwo { get; set; } = true;

    /// <summary>Rendering DPI (default 72). 72 DPI is standard for screen rendering.</summary>
    public int Dpi { get; set; } = 72;

    /// <summary>Face index for font collections (TTC/OTC). 0 = first face.</summary>
    public int FaceIndex { get; set; }

    /// <summary>If true, packs multiple glyphs into separate RGBA channels for smaller textures.</summary>
    public bool ChannelPacking { get; set; }

    /// <summary>If true, renders color font glyphs (COLR/CPAL tables, like emoji fonts).</summary>
    public bool ColorFont { get; set; }

    /// <summary>Which CPAL color palette to use for color fonts. 0 = default palette.</summary>
    public int ColorPaletteIndex { get; set; }

    /// <summary>Variable font axis values, keyed by tag. For example: { "wght", 700 } for bold weight.</summary>
    public Dictionary<string, float>? VariationAxes { get; set; }

    /// <summary>
    /// Super sampling level (1-4). Higher values render at Nx size then
    /// downscale for smoother edges. 1 = no super sampling.
    /// </summary>
    public int SuperSampleLevel { get; set; } = 1;

    /// <summary>
    /// Character to show for missing glyphs. Common choices: '?' or '\uFFFD'.
    /// </summary>
    public char? FallbackCharacter { get; set; }

    /// <summary>Texture format for atlas output (PNG, TGA, or DDS). Default is PNG.</summary>
    public TextureFormat TextureFormat { get; set; } = TextureFormat.Png;

    /// <summary>
    /// If true, enables FreeType hinting for crisp small text.
    /// If false, renders without hinting for smoother curves.
    /// </summary>
    public bool EnableHinting { get; set; } = true;

    /// <summary>
    /// If true, picks the smallest power-of-two texture that fits all glyphs.
    /// Overrides MaxTextureWidth/MaxTextureHeight.
    /// </summary>
    public bool AutofitTexture { get; set; }

    /// <summary>
    /// If true, pads all glyph cells to the same height and aligns them to a common baseline.
    /// </summary>
    public bool EqualizeCellHeights { get; set; }

    /// <summary>
    /// If true, zeroes out all xoffset/yoffset values. Useful for monospace or grid-based rendering.
    /// </summary>
    public bool ForceOffsetsToZero { get; set; }

    /// <summary>
    /// Per-channel control over what goes into each RGBA channel (glyph, outline, both, zero, or one).
    /// </summary>
    public ChannelConfig? Channels { get; set; }

    /// <summary>
    /// Vertical height scaling. 100 = normal, 150 = 50% taller, 75 = 25% shorter.
    /// </summary>
    public int HeightPercent { get; set; } = 100;

    /// <summary>
    /// Custom glyph images keyed by character code. These replace or add glyphs.
    /// You must supply raw pixel data (not encoded PNG/etc).
    /// </summary>
    public Dictionary<int, CustomGlyph>? CustomGlyphs { get; set; }

    /// <summary>
    /// If true, scales the font so the tallest character exactly matches the requested pixel size.
    /// </summary>
    public bool MatchCharHeight { get; set; }

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

    /// <summary>True if gradient start and end colors are both set.</summary>
    internal bool HasGradient => GradientStartR.HasValue && GradientEndR.HasValue;

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

    /// <summary>True if any shadow offset or blur is set.</summary>
    internal bool HasShadow => ShadowOffsetX != 0 || ShadowOffsetY != 0 || ShadowBlur > 0;

    /// <summary>
    /// Expected packing efficiency (0.50 to 0.99). Lower values waste more space but
    /// reduce the chance of needing multiple atlas pages. Default 0.90.
    /// </summary>
    internal float PackingEfficiencyHint { get; set; } = 0.90f;

    /// <summary>Custom font reader. When null, uses the built-in TTF parser.</summary>
    public IFontReader? FontReader { get; set; }

    /// <summary>Custom rasterizer. When null, uses FreeType.</summary>
    public IRasterizer? Rasterizer { get; set; }

    /// <summary>Custom atlas packer. When null, uses the PackingAlgorithm setting.</summary>
    public IAtlasPacker? Packer { get; set; }

    /// <summary>Custom atlas encoder. When null, uses the TextureFormat setting.</summary>
    public IAtlasEncoder? AtlasEncoder { get; set; }

    /// <summary>Extra post-processors to run on each glyph after rasterization.</summary>
    public IReadOnlyList<IGlyphPostProcessor>? PostProcessors { get; set; }

    /// <summary>If true, records how long each pipeline stage takes. Check BmFontResult.Metrics for results.</summary>
    public bool CollectMetrics { get; set; }

    /// <summary>Atlas size constraints (force square, force power-of-two, fixed width).</summary>
    public AtlasSizeConstraints? SizeConstraints { get; set; }

    /// <summary>When set, renders glyphs into a region of an existing PNG image.</summary>
    public AtlasTargetRegion? TargetRegion { get; set; }
}
