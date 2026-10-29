using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Bitmaps;
using KernSmith.Rasterizers.Native.Internal.Color;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// A renderer that draws some glyphs from a source other than their own outline (color layers,
/// bitmaps), so the rasterizer must hand it a glyph even when that outline is blank.
/// </summary>
internal interface IDrawsWithoutOutline
{
    /// <summary>True when this renderer draws the glyph from something other than its outline.</summary>
    bool DrawsWithoutOutline(int glyphIndex);
}

/// <summary>
/// The render mode for color bitmap glyphs (<c>CBDT</c>/<c>sbix</c>, Phase 172): the strike
/// bitmap chosen by <see cref="BitmapGlyphs"/>, scaled to the request by
/// <see cref="BitmapResampler"/>. Glyphs without a bitmap go to the wrapped renderer unchanged.
/// </summary>
/// <remarks>
/// <para>
/// In color mode the output is straight-alpha <see cref="PixelFormat.Rgba32"/>, as for
/// <c>COLR</c>. In coverage mode — a bitmap-only font requested without
/// <see cref="RasterOptions.ColorFont"/> — it is <see cref="PixelFormat.Grayscale8"/> ink
/// coverage: how dark the glyph makes a white background, <c>alpha × (1 − Y)</c> with Y the
/// linear-light Rec. 709 luminance, the same conversion idea as FreeType's gray fallback for
/// color bitmaps.
/// </para>
/// <para>
/// The advance is the glyph's unstyled prepared advance (the <c>hmtx</c> advance at the requested
/// size), as for outline glyphs, so swapping a bitmap glyph for an outline never respaces text.
/// Synthetic bold and italic change neither the pixels nor the advance — the FreeType backend's
/// behavior for bitmap glyphs. LCD and light hinting do not apply. Under
/// <see cref="AntiAliasMode.None"/> alpha (or coverage) is thresholded at 50% and the box is
/// cropped to the ink, as plain mono coverage is.
/// </para>
/// </remarks>
internal sealed class BitmapGlyphRenderer : IGlyphRenderer, IDrawsWithoutOutline
{
    private readonly BitmapGlyphs _bitmaps;
    private readonly GlyphPipeline _pipeline;
    private readonly IGlyphRenderer _fallback;
    private readonly bool _color;

    /// <summary>Wraps the renderer chosen for the request's other options.</summary>
    /// <param name="bitmaps">The face's bitmap strikes.</param>
    /// <param name="pipeline">The face's pipeline, for the unstyled advance of a bold request.</param>
    /// <param name="fallback">Renders glyphs that have no bitmap.</param>
    /// <param name="color">True for RGBA output; false for grayscale ink coverage.</param>
    public BitmapGlyphRenderer(BitmapGlyphs bitmaps, GlyphPipeline pipeline, IGlyphRenderer fallback, bool color)
    {
        ArgumentNullException.ThrowIfNull(bitmaps);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(fallback);
        _bitmaps = bitmaps;
        _pipeline = pipeline;
        _fallback = fallback;
        _color = color;
    }

    /// <inheritdoc />
    public bool DrawsWithoutOutline(int glyphIndex) =>
        _bitmaps.HasBitmap(glyphIndex) || (_fallback is IDrawsWithoutOutline inner && inner.DrawsWithoutOutline(glyphIndex));

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options)
    {
        if (!_bitmaps.TryGetImage(glyph.GlyphIndex, GlyphLimits.PixelsPerEm(options), out var image))
            return _fallback.Measure(glyph, options);

        // A None box depends on which pixels survive the threshold.
        var box = options.AntiAlias == AntiAliasMode.None
            ? Draw(glyph.GlyphIndex, image, options).Box
            : PlacedBox(glyph.GlyphIndex, image, options);
        return Metrics(box, UnstyledAdvance(glyph, options));
    }

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        if (!_bitmaps.TryGetImage(glyphIndex, GlyphLimits.PixelsPerEm(options), out var image))
            return _fallback.Render(codepoint, glyphIndex, glyph, options);

        var (box, pixels) = Draw(glyphIndex, image, options);
        var metrics = Metrics(box, UnstyledAdvance(glyph, options));
        int bytesPerPixel = _color ? 4 : 1;
        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = pixels,
            Width = metrics.Width,
            Height = metrics.Height,
            Pitch = metrics.Width * bytesPerPixel,
            Metrics = metrics,
            Format = _color ? PixelFormat.Rgba32 : PixelFormat.Grayscale8
        };
    }

    /// <summary>The advance without synthetic bold's widening (italic never changes it).</summary>
    private int UnstyledAdvance(PreparedGlyph glyph, RasterOptions options)
    {
        if (!options.Bold)
            return glyph.Advance;

        try
        {
            return _pipeline.TryPrepareBox(glyph.GlyphIndex, options, [], out _, out int advance)
                ? advance
                : _pipeline.Prepare(glyph.GlyphIndex, options, []).Advance;
        }
        catch (Exception ex) when (ex is FontFormatException or ArgumentOutOfRangeException)
        {
            throw new RasterizationException($"Glyph {glyph.GlyphIndex} is malformed: {ex.Message}");
        }
    }

    private static GlyphBox PlacedBox(int glyphIndex, BitmapGlyphImage image, RasterOptions options)
    {
        var box = BitmapResampler.Box(glyphIndex, image);
        GlyphLimits.EnsureGlyphBox(glyphIndex, box.Width, box.Height, options);
        return box;
    }

    private (GlyphBox Box, byte[] Pixels) Draw(int glyphIndex, BitmapGlyphImage image, RasterOptions options)
    {
        var box = PlacedBox(glyphIndex, image, options);
        if (box.IsEmpty)
            return (box, []);

        var rgba = BitmapResampler.Resample(BitmapGlyphs.Decode(glyphIndex, image), image, box);
        bool mono = options.AntiAlias == AntiAliasMode.None;

        if (!_color)
        {
            var coverage = InkCoverage(rgba, mono);
            return mono ? CropToInk(box, coverage, 1) : (box, coverage);
        }

        if (!mono)
            return (box, rgba);

        for (int i = 3; i < rgba.Length; i += 4)
            rgba[i] = rgba[i] >= 128 ? (byte)255 : (byte)0;
        return CropToInk(box, rgba, 4);
    }

    /// <summary>
    /// How dark each pixel makes a white background: alpha × (1 − linear luminance), optionally
    /// thresholded at 50%.
    /// </summary>
    private static byte[] InkCoverage(byte[] rgba, bool threshold)
    {
        var coverage = new byte[rgba.Length / 4];
        for (int i = 0; i < coverage.Length; i++)
        {
            int p = i * 4;
            float luminance = (0.2126f * SrgbTransfer.ToLinear(rgba[p]))
                + (0.7152f * SrgbTransfer.ToLinear(rgba[p + 1]))
                + (0.0722f * SrgbTransfer.ToLinear(rgba[p + 2]));
            int value = Math.Clamp((int)MathF.Round(rgba[p + 3] * (1f - luminance)), 0, 255);
            coverage[i] = threshold ? (value >= 128 ? (byte)255 : (byte)0) : (byte)value;
        }

        return coverage;
    }

    /// <summary>
    /// Drops edge rows and columns with no ink (zero alpha, or zero coverage), as mono coverage
    /// does. A glyph with no ink at all keeps its box.
    /// </summary>
    private static (GlyphBox Box, byte[] Pixels) CropToInk(GlyphBox box, byte[] pixels, int bytesPerPixel)
    {
        int width = box.Width, height = box.Height;
        int inkByte = bytesPerPixel - 1; // alpha for RGBA, the value itself for gray
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[(((y * width) + x) * bytesPerPixel) + inkByte] == 0)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0 || (left == 0 && top == 0 && right == width - 1 && bottom == height - 1))
            return (box, pixels);

        int croppedWidth = right - left + 1, croppedHeight = bottom - top + 1;
        var cropped = new byte[croppedWidth * croppedHeight * bytesPerPixel];
        for (int row = 0; row < croppedHeight; row++)
            Array.Copy(pixels, (((top + row) * width) + left) * bytesPerPixel, cropped, row * croppedWidth * bytesPerPixel, croppedWidth * bytesPerPixel);

        return (new GlyphBox(box.MinX + left, box.MinY + top, croppedWidth, croppedHeight), cropped);
    }

    private static GlyphMetrics Metrics(GlyphBox box, int advance) =>
        box.IsEmpty
            ? new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: advance, Width: 0, Height: 0)
            : new GlyphMetrics(BearingX: box.BearingX, BearingY: box.BearingY, Advance: advance, Width: box.Width, Height: box.Height);
}
