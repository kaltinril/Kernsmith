using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// The default render mode: flattens the outline and fills it with the signed-area scanline
/// rasterizer into 8-bit coverage. <see cref="AntiAliasMode.None"/> thresholds it; Grayscale and
/// Light render as grayscale. <see cref="AntiAliasMode.Lcd"/> goes to <see cref="LcdRenderer"/>.
/// </summary>
internal sealed class CoverageRenderer : IGlyphRenderer
{
    /// <summary>The shared, stateless instance.</summary>
    public static readonly CoverageRenderer Instance = new();

    private CoverageRenderer()
    {
    }

    /// <inheritdoc />
    public GlyphMetrics Measure(PreparedGlyph glyph, RasterOptions options) =>
        new(
            BearingX: glyph.Box.BearingX,
            BearingY: glyph.Box.BearingY,
            Advance: glyph.Advance,
            Width: glyph.Box.Width,
            Height: glyph.Box.Height);

    /// <inheritdoc />
    public RasterizedGlyph Render(int codepoint, int glyphIndex, PreparedGlyph glyph, RasterOptions options)
    {
        var box = glyph.Box;
        var edges = OutlineFlattener.Flatten(glyph.Outline, glyph.ToPixels, includeHorizontal: options.AntiAlias == AntiAliasMode.None);
        var raster = ScanlineRasterizer.Rasterize(
            edges,
            box.Width,
            box.Height,
            box.MinX,
            box.MinY,
            options.AntiAlias,
            box.BearingX,
            box.BearingY);

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
}
