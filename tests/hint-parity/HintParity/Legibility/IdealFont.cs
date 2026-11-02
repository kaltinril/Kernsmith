using KernSmith;

namespace KernSmith.HintParity.Legibility;

/// <summary>
/// The reference a rendering is judged against: the same font rendered unhinted by Native at
/// <see cref="Factor"/>× the size, which is as close to the outline's true shape as a bitmap
/// gets. It supplies the true topology of every glyph, the box-filtered 1× image an ideal
/// (unhinted, perfectly anti-aliased) renderer would produce, and fractional advances and side
/// bearings for spacing. FreeType is not involved, so the score stays independent of it.
/// </summary>
internal sealed class IdealFont
{
    public const int DefaultFactor = 8;

    private readonly Lazy<IReadOnlyDictionary<int, GlyphBitmap>> _downsampled;
    private readonly Lazy<IReadOnlyDictionary<int, GlyphBitmap>> _downsampledMono;
    private readonly Lazy<IReadOnlyDictionary<int, Topology>> _topology;

    private IdealFont(IReadOnlyDictionary<int, GlyphBitmap> highRes, int factor)
    {
        HighRes = highRes;
        Factor = factor;
        _downsampled = new(() => highRes.ToDictionary(kv => kv.Key, kv => LegibilityMetrics.Downsample(kv.Value, factor)));
        _downsampledMono = new(() => _downsampled.Value.ToDictionary(kv => kv.Key, kv => Threshold(kv.Value)));
        _topology = new(() => highRes.ToDictionary(kv => kv.Key, kv => LegibilityMetrics.TopologyOf(kv.Value)));
    }

    /// <summary>The high-resolution glyphs, <see cref="Factor"/>× pixels per 1× pixel.</summary>
    public IReadOnlyDictionary<int, GlyphBitmap> HighRes { get; }

    public int Factor { get; }

    /// <summary>The ideal 1× grayscale image of every glyph.</summary>
    public IReadOnlyDictionary<int, GlyphBitmap> Downsampled => _downsampled.Value;

    /// <summary>The ideal 1× image thresholded at 50%: the reference for a mono rendering.</summary>
    public IReadOnlyDictionary<int, GlyphBitmap> DownsampledMono => _downsampledMono.Value;

    /// <summary>Each glyph's true topology (from the high-resolution image).</summary>
    public IReadOnlyDictionary<int, Topology> Topology => _topology.Value;

    public IReadOnlyDictionary<int, GlyphBitmap> Reference(bool mono) => mono ? DownsampledMono : Downsampled;

    public static IdealFont FromHighRes(IReadOnlyDictionary<int, GlyphBitmap> highRes, int factor) => new(highRes, factor);

    /// <summary>Renders the ideal for a font at a 1× cell size through the public generate path.</summary>
    public static IdealFont Render(byte[] fontData, int size, CharacterSet characters, int factor = DefaultFactor)
    {
        var font = GeneratedFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = size * factor,
            Characters = characters,
            Backend = RasterizerBackend.Native,
            AntiAlias = AntiAliasMode.Grayscale,
            EnableHinting = false,
            HintLevel = HintLevel.None,
            Kerning = false,
        });
        return new IdealFont(font.Glyphs, factor);
    }

    private static GlyphBitmap Threshold(GlyphBitmap g)
    {
        if (g.IsEmpty)
            return g;
        var pixels = new byte[g.Pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = g.Pixels[i] >= LegibilityMetrics.ReadThreshold ? (byte)255 : (byte)0;
        return g with { Pixels = pixels };
    }
}
