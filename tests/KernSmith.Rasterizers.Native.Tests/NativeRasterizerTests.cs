using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

public class NativeRasterizerTests
{
    private static NativeRasterizer LoadedRasterizer()
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        return rasterizer;
    }

    [Fact]
    public void Capabilities_AreMinimal()
    {
        using var rasterizer = new NativeRasterizer();
        var caps = rasterizer.Capabilities;

        caps.SupportsColorFonts.ShouldBeFalse();
        caps.SupportsVariableFonts.ShouldBeFalse();
        caps.SupportsSdf.ShouldBeFalse();
        caps.SupportsOutlineStroke.ShouldBeFalse();
        caps.SupportsSyntheticBold.ShouldBeFalse();
        caps.SupportsSyntheticItalic.ShouldBeFalse();
        caps.SupportsSystemFonts.ShouldBeFalse();
        caps.HandlesOwnSizing.ShouldBeFalse();
        caps.SupportedAntiAliasModes.ShouldBe([AntiAliasMode.None, AntiAliasMode.Grayscale]);
    }

    [Fact]
    public void LoadFont_ParsesFaceAndMapsGlyphs()
    {
        using var rasterizer = LoadedRasterizer();
        rasterizer.GetGlyphIndex('A').ShouldBeGreaterThan(0);
        rasterizer.Face.ShouldNotBeNull();
    }

    [Fact]
    public void LoadFont_Twice_Throws()
    {
        using var rasterizer = LoadedRasterizer();
        Should.Throw<InvalidOperationException>(() => rasterizer.LoadFont(TestFonts.RobotoRegularBytes()));
    }

    [Fact]
    public void LoadSystemFont_NotSupported()
    {
        using var rasterizer = new NativeRasterizer();
        Should.Throw<NotSupportedException>(() => rasterizer.LoadSystemFont("Arial"));
    }

    [Fact]
    public void RasterizeGlyph_ProducesGrayscaleBitmap()
    {
        using var rasterizer = LoadedRasterizer();
        var glyph = rasterizer.RasterizeGlyph('A', new RasterOptions { Size = 32 });

        glyph.ShouldNotBeNull();
        glyph!.Codepoint.ShouldBe('A');
        glyph.GlyphIndex.ShouldBeGreaterThan(0);
        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        glyph.Pitch.ShouldBe(glyph.Width);
        glyph.BitmapData.Length.ShouldBe(glyph.Width * glyph.Height);
        glyph.BitmapData.ShouldContain(b => b > 0, "'A' rendered blank");
        glyph.Metrics.Width.ShouldBe(glyph.Width);
        glyph.Metrics.Height.ShouldBe(glyph.Height);
        glyph.Metrics.Advance.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RasterizeGlyph_UsesTightCoverageBounds()
    {
        // The control-hull bbox from GlyphOutline over-estimates: a curve stays inside its
        // controls, so the outer rows/columns would come back empty. Every edge row and column
        // of the bitmap must carry ink.
        using var rasterizer = LoadedRasterizer();
        var glyph = rasterizer.RasterizeGlyph('O', new RasterOptions { Size = 48 });
        glyph.ShouldNotBeNull();

        int width = glyph!.Width, height = glyph.Height;
        RowHasInk(glyph, 0).ShouldBeTrue("top row is empty");
        RowHasInk(glyph, height - 1).ShouldBeTrue("bottom row is empty");
        ColumnHasInk(glyph, 0).ShouldBeTrue("left column is empty");
        ColumnHasInk(glyph, width - 1).ShouldBeTrue("right column is empty");

        static bool RowHasInk(RasterizedGlyph g, int y)
        {
            for (int x = 0; x < g.Width; x++)
                if (g.BitmapData[(y * g.Pitch) + x] > 0) return true;
            return false;
        }

        static bool ColumnHasInk(RasterizedGlyph g, int x)
        {
            for (int y = 0; y < g.Height; y++)
                if (g.BitmapData[(y * g.Pitch) + x] > 0) return true;
            return false;
        }
    }

    [Fact]
    public void RasterizeGlyph_Space_HasAdvanceButNoBitmap()
    {
        using var rasterizer = LoadedRasterizer();
        var glyph = rasterizer.RasterizeGlyph(' ', new RasterOptions { Size = 32 });

        glyph.ShouldNotBeNull();
        glyph!.Width.ShouldBe(0);
        glyph.Height.ShouldBe(0);
        glyph.Pitch.ShouldBe(0);
        glyph.BitmapData.ShouldBeEmpty();
        glyph.Metrics.Advance.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void RasterizeGlyph_MissingCodepoint_ReturnsNull()
    {
        using IRasterizer rasterizer = LoadedRasterizer();
        rasterizer.RasterizeGlyph(0x1F600, new RasterOptions { Size = 32 }).ShouldBeNull();
        rasterizer.GetGlyphMetrics(0x1F600, new RasterOptions { Size = 32 }).ShouldBeNull();
    }

    [Fact]
    public void RasterizeGlyph_AntiAliasNone_ProducesOnlyBlackAndWhite()
    {
        using var rasterizer = LoadedRasterizer();
        var glyph = rasterizer.RasterizeGlyph(
            'A', new RasterOptions { Size = 32, AntiAlias = AntiAliasMode.None });

        glyph.ShouldNotBeNull();
        glyph!.BitmapData.ShouldAllBe(b => b == 0 || b == 255);
        glyph.BitmapData.ShouldContain(b => b == 255);
    }

    [Fact]
    public void RasterizeAll_MatchesPerGlyphRasterizeGlyph()
    {
        using var rasterizer = LoadedRasterizer();
        var options = new RasterOptions { Size = 24 };
        int[] codepoints = ['A', 'g', ' ', '@', 0x1F600];

        var batch = rasterizer.RasterizeAll(codepoints, options);

        // The emoji is absent from Roboto and must be skipped, not returned blank.
        batch.Count.ShouldBe(4);
        foreach (var glyph in batch)
        {
            var single = rasterizer.RasterizeGlyph(glyph.Codepoint, options);
            single.ShouldNotBeNull();
            glyph.Width.ShouldBe(single!.Width);
            glyph.Height.ShouldBe(single.Height);
            glyph.Metrics.ShouldBe(single.Metrics);
            glyph.BitmapData.ShouldBe(single.BitmapData);
        }
    }

    [Fact]
    public void GetGlyphMetrics_MatchesRasterizedGlyph()
    {
        using IRasterizer rasterizer = LoadedRasterizer();
        var options = new RasterOptions { Size = 32 };

        foreach (char character in "AWgj.@")
        {
            var metrics = rasterizer.GetGlyphMetrics(character, options);
            var glyph = rasterizer.RasterizeGlyph(character, options);

            metrics.ShouldNotBeNull();
            glyph.ShouldNotBeNull();
            metrics!.Value.ShouldBe(glyph!.Metrics, $"metrics for '{character}' diverge from the rendered glyph");
        }
    }

    [Fact]
    public void GetKerningPairs_ReturnsNull_SoTheSharedParserRuns()
    {
        using IRasterizer rasterizer = LoadedRasterizer();
        rasterizer.GetKerningPairs(new RasterOptions { Size = 32 }).ShouldBeNull();
    }

    [Fact]
    public void LoadFont_CffFont_ThrowsRasterizationException()
    {
        using var rasterizer = new NativeRasterizer();

        var ex = Should.Throw<RasterizationException>(() => rasterizer.LoadFont(SyntheticFonts.CffFlavoured()));
        ex.Message.ShouldContain("CFF");
        ex.Message.ShouldContain("166", Case.Insensitive);
    }

    [Fact]
    public void GetFontMetrics_WithoutFont_Throws()
    {
        using IRasterizer rasterizer = new NativeRasterizer();
        Should.Throw<InvalidOperationException>(() => rasterizer.GetFontMetrics(new RasterOptions { Size = 32 }));
    }

    [Fact]
    public void RasterizeGlyph_WithoutFont_Throws()
    {
        using var rasterizer = new NativeRasterizer();
        Should.Throw<InvalidOperationException>(() => rasterizer.RasterizeGlyph('A', new RasterOptions { Size = 32 }));
    }

    [Fact]
    public void Dispose_ThenUse_Throws()
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.Dispose();
        Should.Throw<ObjectDisposedException>(() => rasterizer.LoadFont(TestFonts.RobotoRegularBytes()));
    }

    [Fact]
    public void Factory_CanCreateNativeBackend()
    {
        // Reference the assembly type so the module initializer runs even under trimming-style loads.
        _ = typeof(NativeRasterizer);

        using var rasterizer = RasterizerFactory.Create(RasterizerBackend.Native);
        rasterizer.ShouldBeOfType<NativeRasterizer>();
    }
}
