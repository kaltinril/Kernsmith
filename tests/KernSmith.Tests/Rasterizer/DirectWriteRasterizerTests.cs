#if DIRECTWRITE
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.DirectWrite.TerraFX;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Tests for the DirectWrite rasterizer backend (Windows-only, net10.0-windows).
/// These tests compile and run only on the net10.0-windows TFM.
/// </summary>
[Collection("RasterizerFactory")]
public class DirectWriteRasterizerTests : IDisposable
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(FixturesDir, "Roboto-Regular.ttf"));

    private static RasterOptions DefaultOptions => new()
    {
        Size = 32,
        AntiAlias = AntiAliasMode.Grayscale
    };

    private DirectWriteRasterizer? _rasterizer;

    public void Dispose()
    {
        _rasterizer?.Dispose();
    }

    // -- 1. Factory registration -----------------------------------------

    [Fact]
    public void Factory_Create_DirectWrite_ReturnsDirectWriteRasterizer()
    {
        using var rasterizer = RasterizerFactory.Create(RasterizerBackend.DirectWrite);

        rasterizer.ShouldNotBeNull();
        rasterizer.ShouldBeOfType<DirectWriteRasterizer>();
    }

    // -- 2. Factory reset isolates state ---------------------------------

    [Fact]
    public void Factory_ResetForTesting_ClearsAndRediscovers()
    {
        try
        {
            RasterizerFactory.ResetForTesting();
            var rasterizer = RasterizerFactory.Create(RasterizerBackend.DirectWrite);
            rasterizer.ShouldBeOfType<DirectWriteRasterizer>();
        }
        finally
        {
            RasterizerFactory.ResetForTesting();
        }
    }

    // -- 3. LoadFont succeeds --------------------------------------------

    [Fact]
    public void LoadFont_WithValidFont_DoesNotThrow()
    {
        _rasterizer = new DirectWriteRasterizer();
        var fontData = LoadTestFont();

        Should.NotThrow(() => _rasterizer.LoadFont(fontData));
    }

    // -- 4. RasterizeGlyph produces output --------------------------------

    [Fact]
    public void RasterizeGlyph_LetterA_ProducesNonEmptyBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var glyph = _rasterizer.RasterizeGlyph(65, DefaultOptions); // 'A'

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        glyph.BitmapData.Length.ShouldBeGreaterThan(0);
    }

    // -- 5. RasterizeGlyph for missing codepoint --------------------------

    [Fact]
    public void RasterizeGlyph_MissingCodepoint_ReturnsNullOrNotdefGlyph()
    {
        _rasterizer = CreateAndLoadRasterizer();

        // Use a Private Use Area codepoint unlikely to be in Roboto.
        var glyph = _rasterizer.RasterizeGlyph(0xFDD0, DefaultOptions);

        // DirectWrite behavior: either null or a .notdef placeholder glyph.
        // Both outcomes are acceptable; verify the rasterizer doesn't crash.
        if (glyph is not null)
        {
            // If a .notdef glyph is returned, it should still have valid structure.
            glyph.Codepoint.ShouldBe(0xFDD0);
            glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        }
    }

    // -- 6. RasterizeAll produces multiple glyphs -------------------------

    [Fact]
    public void RasterizeAll_ThreeCodepoints_ReturnsThreeGlyphs()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var codepoints = new[] { 65, 66, 67 }; // A, B, C

        var results = _rasterizer.RasterizeAll(codepoints, DefaultOptions);

        results.Count.ShouldBe(3);
    }

    // -- 7. GetGlyphMetrics returns metrics without bitmap -----------------

    [Fact]
    public void GetGlyphMetrics_LetterA_ReturnsPositiveAdvance()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var metrics = _rasterizer.GetGlyphMetrics(65, DefaultOptions); // 'A'

        metrics.ShouldNotBeNull();
        metrics.Value.Advance.ShouldBeGreaterThan(0);
    }

    // -- 8. Capabilities reports correctly --------------------------------

    [Fact]
    public void Capabilities_ReportsDirectWriteCapabilities()
    {
        _rasterizer = new DirectWriteRasterizer();

        var caps = _rasterizer.Capabilities;

        caps.SupportsColorFonts.ShouldBeFalse();
        caps.SupportsVariableFonts.ShouldBeFalse();
        caps.SupportsSdf.ShouldBeFalse();
        caps.SupportsOutlineStroke.ShouldBeFalse();
        caps.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.None);
        caps.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.Grayscale);
    }

    // -- 9. Grayscale values are remapped to 0-255 ------------------------

    [Fact]
    public void RasterizeGlyph_GrayscaleValues_RemappedTo0Through255()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var glyph = _rasterizer.RasterizeGlyph(65, DefaultOptions); // 'A'

        glyph.ShouldNotBeNull();
        // After rendering, fully opaque pixels should reach high values
        // (close to 255), proving proper grayscale output.
        var maxValue = glyph.BitmapData.Max();
        maxValue.ShouldBeGreaterThan((byte)64);
    }

    // -- 10. Dispose releases resources -----------------------------------

    [Fact]
    public void Dispose_ThenRasterize_ThrowsObjectDisposedException()
    {
        var rasterizer = CreateAndLoadRasterizer();
        rasterizer.Dispose();

        Should.Throw<ObjectDisposedException>(
            () => rasterizer.RasterizeGlyph(65, DefaultOptions));
    }

    [Fact]
    public void Dispose_ThenLoadFont_ThrowsObjectDisposedException()
    {
        var rasterizer = new DirectWriteRasterizer();
        rasterizer.Dispose();

        Should.Throw<ObjectDisposedException>(
            () => rasterizer.LoadFont(LoadTestFont()));
    }

    [Fact]
    public void Dispose_ThenGetMetrics_ThrowsObjectDisposedException()
    {
        var rasterizer = CreateAndLoadRasterizer();
        rasterizer.Dispose();

        Should.Throw<ObjectDisposedException>(
            () => rasterizer.GetGlyphMetrics(65, DefaultOptions));
    }

    // -- 11. Pixel format is Grayscale8 -----------------------------------

    [Fact]
    public void RasterizeGlyph_Format_IsGrayscale8()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var glyph = _rasterizer.RasterizeGlyph(65, DefaultOptions);

        glyph.ShouldNotBeNull();
        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
    }

    // -- Helpers ----------------------------------------------------------

    private static DirectWriteRasterizer CreateAndLoadRasterizer()
    {
        var rasterizer = new DirectWriteRasterizer();
        rasterizer.LoadFont(LoadTestFont());
        return rasterizer;
    }
}
#endif
