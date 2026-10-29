using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// The default render mode: flattens the outline and fills it with the signed-area scanline
/// rasterizer into 8-bit coverage. <see cref="AntiAliasMode.None"/> thresholds it and crops the
/// glyph box to the ink (<see cref="ScanlineRasterizer.RasterizeMono"/>); Grayscale and Light
/// render as grayscale. <see cref="AntiAliasMode.Lcd"/> goes to <see cref="LcdRenderer"/>.
/// </summary>
/// <remarks>
/// A <see cref="AntiAliasMode.None"/> box depends on which pixels pass the threshold, so
/// <see cref="Measure"/> renders it too (into pooled scratch) to stay equal to <see cref="Render"/>.
/// </remarks>
internal sealed class CoverageRenderer : IGlyphRenderer
{
    /// <summary>The shared, stateless instance.</summary>
    public static readonly CoverageRenderer Instance = new();

    private CoverageRenderer()
    {
    }

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options)
    {
        var box = glyph.Box;
        if (options.AntiAlias != AntiAliasMode.None)
            return new(BearingX: box.BearingX, BearingY: box.BearingY, Advance: glyph.Advance, Width: box.Width, Height: box.Height);

        var ink = ScanlineRasterizer.MeasureMono(Edges(glyph, options), box.Width, box.Height, box.MinX, box.MinY, box.BearingX, box.BearingY);
        return new(BearingX: ink.BearingX, BearingY: ink.BearingY, Advance: glyph.Advance, Width: ink.Width, Height: ink.Height);
    }

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        var box = glyph.Box;
        var edges = Edges(glyph, options);
        var raster = options.AntiAlias == AntiAliasMode.None
            ? ScanlineRasterizer.RasterizeMono(edges, box.Width, box.Height, box.MinX, box.MinY, box.BearingX, box.BearingY)
            : ScanlineRasterizer.Rasterize(edges, box.Width, box.Height, box.MinX, box.MinY, options.AntiAlias, box.BearingX, box.BearingY);

        return new RasterizedGlyph
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = raster.Bitmap,
            Width = raster.Width,
            Height = raster.Height,
            Pitch = raster.Width,
            Metrics = new GlyphMetrics(
                BearingX: raster.BearingX,
                BearingY: raster.BearingY,
                Advance: glyph.Advance,
                Width: raster.Width,
                Height: raster.Height),
            Format = PixelFormat.Grayscale8
        };
    }

    private static EdgeSegment[] Edges(PreparedGlyph glyph, RasterOptions options) =>
        OutlineFlattener.Flatten(glyph.Outline, glyph.ToPixels, includeHorizontal: options.AntiAlias == AntiAliasMode.None);
}
