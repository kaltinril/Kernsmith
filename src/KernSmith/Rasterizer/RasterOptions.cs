namespace KernSmith.Rasterizer;

/// <summary>
/// Settings for rendering characters: size, DPI, effects, and variable font axes.
/// </summary>
public sealed record RasterOptions
{
    /// <summary>Font size in points.</summary>
    public required int Size { get; init; }

    /// <summary>DPI for converting points to pixels. Default is 72 (1 point = 1 pixel).</summary>
    public int Dpi { get; init; } = 72;

    /// <summary>Anti-aliasing mode. Default is grayscale.</summary>
    public AntiAliasMode AntiAlias { get; init; } = AntiAliasMode.Grayscale;

    /// <summary>If true, applies synthetic bold by thickening glyph strokes.</summary>
    public bool Bold { get; init; }

    /// <summary>If true, applies synthetic italic by slanting glyph outlines.</summary>
    public bool Italic { get; init; }

    /// <summary>If true, render as signed distance fields instead of regular bitmaps.</summary>
    public bool Sdf { get; init; }

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
    /// BMFont's "aa" setting maps to this value.
    /// </summary>
    public int SuperSample { get; init; } = 1;

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
            Bold = options.Bold,
            Italic = options.Italic,
            Sdf = options.Sdf,
            ColorFont = options.ColorFont,
            ColorPaletteIndex = options.ColorPaletteIndex,
            VariationAxes = options.VariationAxes,
            EnableHinting = options.EnableHinting,
            SuperSample = options.SuperSampleLevel
        };
    }
}
