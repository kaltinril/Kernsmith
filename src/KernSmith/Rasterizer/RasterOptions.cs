namespace KernSmith.Rasterizer;

/// <summary>
/// Settings for rendering characters: size, DPI, effects, and variable font axes.
/// </summary>
public sealed record RasterOptions
{
    /// <summary>Font size in points. Fractional values are supported by all rasterizer backends
    /// except GDI, which rounds to the nearest integer pixel size.</summary>
    public required float Size { get; init; }

    /// <summary>DPI for converting points to pixels. Default is 72 (1 point = 1 pixel).</summary>
    public int Dpi { get; init; } = 72;

    /// <summary>Anti-aliasing mode. Default is grayscale.</summary>
    public AntiAliasMode AntiAlias { get; init; } = AntiAliasMode.Grayscale;

    /// <summary>
    /// The low-pass filter for <see cref="AntiAliasMode.Lcd"/>. Default is
    /// <see cref="KernSmith.LcdFilter.Default"/> (the backend's choice). Only consumed by
    /// backends that render subpixel output (Native); others ignore it.
    /// </summary>
    public LcdFilter LcdFilter { get; init; }

    /// <summary>
    /// The display's subpixel layout for <see cref="AntiAliasMode.Lcd"/>. Default is
    /// <see cref="KernSmith.LcdSubpixelOrder.Rgb"/>. Only consumed by backends that render
    /// subpixel output (Native); others ignore it.
    /// </summary>
    public LcdSubpixelOrder LcdSubpixelOrder { get; init; }

    /// <summary>If true, applies bold (native face or synthetic).</summary>
    public bool Bold { get; init; }

    /// <summary>If true, applies italic (native face or synthetic).</summary>
    public bool Italic { get; init; }

    /// <summary>If true, forces synthetic bold even on natively-bold fonts or system font paths.</summary>
    public bool ForceSyntheticBold { get; init; }

    /// <summary>If true, forces synthetic italic even on natively-italic fonts or system font paths.</summary>
    public bool ForceSyntheticItalic { get; init; }

    /// <summary>
    /// If true, render single-channel signed distance fields instead of regular bitmaps.
    /// <see cref="FromGeneratorOptions"/> sets it exactly when the effective mode is
    /// <see cref="KernSmith.SdfMode.Sdf"/>, so a backend that predates <see cref="SdfMode"/> can keep
    /// reading this flag alone.
    /// </summary>
    public bool Sdf { get; init; }

    /// <summary>
    /// The distance field to render. When <see cref="KernSmith.SdfMode.None"/>, <see cref="Sdf"/>
    /// decides; backends should switch on <see cref="EffectiveSdfMode"/>.
    /// </summary>
    public SdfMode SdfMode { get; init; }

    /// <summary>
    /// The distance field requested: <see cref="SdfMode"/> when set, otherwise
    /// <see cref="KernSmith.SdfMode.Sdf"/> if <see cref="Sdf"/> is true, otherwise none.
    /// </summary>
    public SdfMode EffectiveSdfMode =>
        SdfMode != SdfMode.None ? SdfMode : Sdf ? SdfMode.Sdf : SdfMode.None;

    /// <summary>
    /// SDF supersampling scale factor. Higher values render the distance field at a larger
    /// size for finer precision. Default is 1 (no scaling). Only consumed by backends that
    /// support SDF (e.g., FreeType); other backends ignore it.
    /// </summary>
    public int SdfScale { get; init; } = 1;

    /// <summary>
    /// SDF spread (search radius) in pixels. Larger values capture distance information
    /// further from the glyph edge. Default is 8 (the FreeType SDF default). Only consumed
    /// by backends that support SDF; other backends ignore it.
    /// </summary>
    public float SdfSpread { get; init; } = 8f;

    /// <summary>
    /// Gamma correction applied to rasterized coverage/alpha values. Default is 1.8 (matching
    /// Hiero), which the FreeType backend treats as the no-op baseline so that default output
    /// is unchanged. Only consumed by the FreeType backend; other backends ignore it.
    /// </summary>
    public float Gamma { get; init; } = 1.8f;

    /// <summary>If true, render color glyphs (emoji and other multi-color characters).</summary>
    public bool ColorFont { get; init; }

    /// <summary>Zero-based CPAL color palette index for color fonts. Default is 0.</summary>
    public int ColorPaletteIndex { get; init; }

    /// <summary>Variable font axis overrides. For example, {"wght", 700} for bold weight.</summary>
    public Dictionary<string, float>? VariationAxes { get; init; }

    /// <summary>If true, enables font hinting for sharper rendering at small sizes. Default is true.</summary>
    public bool EnableHinting { get; init; } = true;

    /// <summary>
    /// Supersampling factor for higher quality rendering. The rasterizer renders at
    /// Size * SuperSample internally and downscales. Default is 1 (no supersampling).
    /// BMFont's "aa" setting maps to this value. KernSmith core super samples glyphs itself (it
    /// asks for them at Size x level and box-filters them down), so its glyph requests always
    /// carry 1; only <see cref="IRasterizer.GetFontMetrics"/> and
    /// <see cref="IRasterizer.GetKerningPairs"/> see the level, at the final Size. With
    /// <see cref="AntiAliasMode.Lcd"/> nothing is super sampled: LCD renders at the final size.
    /// </summary>
    public int SuperSample { get; init; } = 1;

    /// <summary>
    /// Horizontal scale of every outline and advance as a percentage (condensed below 100, expanded
    /// above). Default 100. Only consumed by backends that report
    /// <see cref="IRasterizerCapabilities.SupportsWidthScale"/>; the pipeline never sends another
    /// value to any other backend.
    /// </summary>
    public int WidthPercent { get; init; } = 100;

    /// <summary>
    /// Slant of synthetic italic in degrees from vertical, positive leaning right. Default 12, which
    /// every backend applies its own way. Only consumed by backends that report
    /// <see cref="IRasterizerCapabilities.SupportsItalicAngle"/>; the pipeline never sends another
    /// value with <see cref="Italic"/> to any other backend.
    /// </summary>
    public float ItalicAngle { get; init; } = 12f;

    /// <summary>
    /// Creates a <see cref="RasterOptions"/> from a <see cref="FontGeneratorOptions"/>.
    /// </summary>
    public static RasterOptions FromGeneratorOptions(FontGeneratorOptions options)
    {
        return new RasterOptions
        {
            Size = options.Size,
            Dpi = options.Dpi,
            AntiAlias = options.AntiAlias,
            LcdFilter = options.LcdFilter,
            LcdSubpixelOrder = options.LcdSubpixelOrder,
            Bold = options.Bold,
            Italic = options.Italic,
            ForceSyntheticBold = options.ForceSyntheticBold,
            ForceSyntheticItalic = options.ForceSyntheticItalic,
            Sdf = options.EffectiveSdfMode == SdfMode.Sdf,
            SdfMode = options.EffectiveSdfMode,
            SdfScale = options.SdfScale,
            SdfSpread = options.SdfSpread,
            Gamma = options.Gamma,
            ColorFont = options.ColorFont,
            ColorPaletteIndex = options.ColorPaletteIndex,
            VariationAxes = options.VariationAxes,
            EnableHinting = options.EnableHinting,
            SuperSample = options.SuperSampleLevel,
            WidthPercent = options.WidthPercent,
            ItalicAngle = options.ItalicAngle
        };
    }
}
