namespace Bmfontier.Rasterizer;

/// <summary>
/// Options passed to the rasterizer, extracted from <see cref="FontGeneratorOptions"/>.
/// </summary>
public sealed record RasterOptions
{
    public required int Size { get; init; }
    public int Dpi { get; init; } = 72;
    public AntiAliasMode AntiAlias { get; init; } = AntiAliasMode.Grayscale;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Sdf { get; init; }
    public bool ColorFont { get; init; }
    public int ColorPaletteIndex { get; init; }
    public Dictionary<string, float>? VariationAxes { get; init; }

    /// <summary>
    /// Creates a <see cref="RasterOptions"/> from a <see cref="FontGeneratorOptions"/> instance.
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
            VariationAxes = options.VariationAxes
        };
    }
}
