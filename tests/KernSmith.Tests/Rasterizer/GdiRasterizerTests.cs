#if WINDOWS
using System.Runtime.CompilerServices;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Gdi;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Tests for the GDI rasterizer backend (Windows-only).
/// These tests compile and run only on Windows TFMs (net8.0-windows, net10.0-windows).
/// </summary>
[Collection("RasterizerFactory")]
public class GdiRasterizerTests : IDisposable
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

    private GdiRasterizer? _rasterizer;

    /// <summary>
    /// Ensures the GDI backend is registered with the factory. The module initializer
    /// fires once per process, but <see cref="RasterizerFactory.ResetForTesting"/> in
    /// other tests' Dispose can remove it. This method re-registers if needed.
    /// </summary>
    private static void EnsureGdiRegistered()
    {
        // Reference GdiRegistration to guarantee the assembly is loaded and the
        // module initializer has executed at least once.
        RuntimeHelpers.RunClassConstructor(typeof(GdiRasterizer).TypeHandle);

        if (!RasterizerFactory.GetAvailableBackends().Contains(RasterizerBackend.Gdi))
            RasterizerFactory.Register(RasterizerBackend.Gdi, () => new GdiRasterizer());
    }

    public void Dispose()
    {
        _rasterizer?.Dispose();
    }

    // ── 1. Factory registration ─────────────────────────────────────

    [Fact]
    public void Factory_Create_Gdi_ReturnsGdiRasterizer()
    {
        EnsureGdiRegistered();

        using var rasterizer = RasterizerFactory.Create(RasterizerBackend.Gdi);

        rasterizer.ShouldNotBeNull();
        rasterizer.ShouldBeOfType<GdiRasterizer>();
    }

    // ── 2. Factory reset isolates state ─────────────────────────────

    [Fact]
    public void Factory_ResetForTesting_RemovesGdiRegistration()
    {
        EnsureGdiRegistered();

        // GDI should be available before reset.
        RasterizerFactory.GetAvailableBackends().ShouldContain(RasterizerBackend.Gdi);

        RasterizerFactory.ResetForTesting();

        try
        {
            // After reset, ALL backends are cleared (including FreeType).
            RasterizerFactory.GetAvailableBackends().ShouldBeEmpty();

            var act = () => RasterizerFactory.Create(RasterizerBackend.Gdi);
            Should.Throw<InvalidOperationException>(act);
        }
        finally
        {
            // Re-register all backends to restore state for other tests.
            FreeTypeRegistration.Register();
            RasterizerFactory.Register(RasterizerBackend.Gdi, () => new GdiRasterizer());
        }
    }

    // ── 3. LoadFont succeeds ────────────────────────────────────────

    [Fact]
    public void LoadFont_WithValidFont_DoesNotThrow()
    {
        _rasterizer = new GdiRasterizer();
        var fontData = LoadTestFont();

        Should.NotThrow(() => _rasterizer.LoadFont(fontData));
    }

    // ── 4. RasterizeGlyph produces output ───────────────────────────

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

    // ── 5. RasterizeGlyph for missing codepoint ─────────────────────

    [Fact]
    public void RasterizeGlyph_MissingCodepoint_ReturnsNullOrNotdefGlyph()
    {
        _rasterizer = CreateAndLoadRasterizer();

        // GDI renders the .notdef glyph for unmapped codepoints rather than
        // returning null (only truly invalid glyph requests return GDI_ERROR).
        // Use a Private Use Area codepoint unlikely to be in Roboto.
        var glyph = _rasterizer.RasterizeGlyph(0xFDD0, DefaultOptions);

        // GDI behavior: either null (GDI_ERROR) or a .notdef placeholder glyph.
        // Both outcomes are acceptable; verify the rasterizer doesn't crash.
        if (glyph is not null)
        {
            // If a .notdef glyph is returned, it should still have valid structure.
            glyph.Codepoint.ShouldBe(0xFDD0);
            glyph.Format.ShouldBe(PixelFormat.Grayscale8);
        }
    }

    // ── 6. RasterizeAll produces multiple glyphs ────────────────────

    [Fact]
    public void RasterizeAll_ThreeCodepoints_ReturnsThreeGlyphs()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var codepoints = new[] { 65, 66, 67 }; // A, B, C

        var results = _rasterizer.RasterizeAll(codepoints, DefaultOptions);

        results.Count.ShouldBe(3);
    }

    // ── 7. GetGlyphMetrics returns metrics without bitmap ───────────

    [Fact]
    public void GetGlyphMetrics_LetterA_ReturnsPositiveAdvance()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var metrics = _rasterizer.GetGlyphMetrics(65, DefaultOptions); // 'A'

        metrics.ShouldNotBeNull();
        metrics.Value.Advance.ShouldBeGreaterThan(0);
    }

    // ── 8. Capabilities reports correctly ───────────────────────────

    [Fact]
    public void Capabilities_ReportsGdiLimitations()
    {
        _rasterizer = new GdiRasterizer();

        var caps = _rasterizer.Capabilities;

        caps.SupportsColorFonts.ShouldBeFalse();
        caps.SupportsVariableFonts.ShouldBeFalse();
        caps.SupportsSdf.ShouldBeFalse();
        caps.SupportsOutlineStroke.ShouldBeFalse();
        caps.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.None);
        caps.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.Grayscale);
    }

    // ── 9. Grayscale values are remapped to 0-255 ──────────────────

    [Fact]
    public void RasterizeGlyph_GrayscaleValues_RemappedTo0Through255()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var glyph = _rasterizer.RasterizeGlyph(65, DefaultOptions); // 'A'

        glyph.ShouldNotBeNull();
        // After remapping from GDI's 0-64 range to 0-255, fully opaque pixels
        // should have values well above 64 (proving the remap happened).
        var maxValue = glyph.BitmapData.Max();
        maxValue.ShouldBeGreaterThan((byte)64);
    }

    // ── 10. Dispose releases resources ──────────────────────────────

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
        var rasterizer = new GdiRasterizer();
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

    // ── 11. Bold flag affects output ────────────────────────────────

    [Fact]
    public void RasterizeGlyph_Bold_AdvanceIsGreaterThanOrEqualToNormal()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var normalOptions = new RasterOptions { Size = 32 };
        var boldOptions = new RasterOptions { Size = 32, Bold = true };

        var normalGlyph = _rasterizer.RasterizeGlyph(65, normalOptions);
        var boldGlyph = _rasterizer.RasterizeGlyph(65, boldOptions);

        normalGlyph.ShouldNotBeNull();
        boldGlyph.ShouldNotBeNull();
        boldGlyph!.Metrics.Advance.ShouldBeGreaterThanOrEqualTo(normalGlyph!.Metrics.Advance);
    }

    // ── 12. Pixel format is Grayscale8 ──────────────────────────────

    [Fact]
    public void RasterizeGlyph_Format_IsGrayscale8()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var glyph = _rasterizer.RasterizeGlyph(65, DefaultOptions);

        glyph.ShouldNotBeNull();
        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static GdiRasterizer CreateAndLoadRasterizer()
    {
        var rasterizer = new GdiRasterizer();
        rasterizer.LoadFont(LoadTestFont());
        return rasterizer;
    }
}
#endif
