using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// FreeType's own COLR path re-loads each layer glyph inside <c>FT_Render_Glyph</c>, so a synthetic
/// bold or oblique applied to the base glyph's outline in the slot never reaches the color layers.
/// </summary>
public class FreeTypeColorStyleTests
{
    private const int Size = 48;

    private static byte[] Bungee() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Color", "BungeeColor-Regular.ttf"));

    private static RasterOptions Options(bool bold = false, bool italic = false, AntiAliasMode antiAlias = AntiAliasMode.Grayscale) =>
        new() { Size = Size, Dpi = 72, ColorFont = true, Bold = bold, Italic = italic, AntiAlias = antiAlias };

    private static RasterizedGlyph Render(RasterOptions options, int codepoint = 'A')
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(Bungee());
        var glyph = rasterizer.RasterizeGlyph(codepoint, options);
        glyph.ShouldNotBeNull();
        return glyph;
    }

    private static int Ink(RasterizedGlyph glyph)
    {
        var ink = 0;
        for (var i = 3; i < glyph.BitmapData.Length; i += 4)
        {
            if (glyph.BitmapData[i] > 127)
                ink++;
        }
        return ink;
    }

    /// <summary>The opaque colors by how often they occur, commonest first.</summary>
    private static int[] OpaqueColors(RasterizedGlyph glyph)
    {
        var counts = new Dictionary<int, int>();
        for (var i = 0; i + 3 < glyph.BitmapData.Length; i += 4)
        {
            if (glyph.BitmapData[i + 3] != 255)
                continue;
            var color = glyph.BitmapData[i] << 16 | glyph.BitmapData[i + 1] << 8 | glyph.BitmapData[i + 2];
            counts[color] = counts.GetValueOrDefault(color) + 1;
        }
        return [.. counts.OrderByDescending(c => c.Value).Select(c => c.Key)];
    }

    /// <summary>The style keeps the main layer's palette color and more than one color overall.</summary>
    private static void ShouldKeepLayerColors(RasterizedGlyph styled, RasterizedGlyph plain)
    {
        var colors = OpaqueColors(styled);
        colors.Length.ShouldBeGreaterThan(1, "the styled glyph lost its layer colors");
        colors[0].ShouldBe(OpaqueColors(plain)[0], "the main layer keeps its palette color");
    }

    [Fact]
    public void SyntheticBold_ThickensTheColorLayers()
    {
        var plain = Render(Options());
        var bold = Render(Options(bold: true));

        bold.Format.ShouldBe(PixelFormat.Rgba32);
        Ink(bold).ShouldBeGreaterThan(Ink(plain) * 21 / 20, "bold must add ink to the color layers");
        bold.Width.ShouldBeGreaterThan(plain.Width);
        ShouldKeepLayerColors(bold, plain);
    }

    [Fact]
    public void SyntheticItalic_ShearsTheColorLayers()
    {
        var plain = Render(Options());
        var italic = Render(Options(italic: true));

        italic.Format.ShouldBe(PixelFormat.Rgba32);
        italic.Width.ShouldBeGreaterThan(plain.Width, "a sheared glyph is wider");
        ShouldKeepLayerColors(italic, plain);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StyledColorGlyph_MetricsMatchTheBitmap(bool bold, bool italic)
    {
        var options = Options(bold, italic);
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(Bungee());

        var metrics = rasterizer.GetGlyphMetrics('A', options);
        var glyph = rasterizer.RasterizeGlyph('A', options);

        metrics.ShouldBe(glyph!.Metrics);
    }
}
