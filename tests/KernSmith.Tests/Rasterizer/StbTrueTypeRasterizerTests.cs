using System.Runtime.CompilerServices;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Tests for the StbTrueType rasterizer backend (cross-platform, pure C#).
/// </summary>
[Collection("RasterizerFactory")]
public class StbTrueTypeRasterizerTests : IDisposable
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

    private StbTrueTypeRasterizer? _rasterizer;

    /// <summary>
    /// Ensures the StbTrueType backend is registered with the factory. The module initializer
    /// fires once per process, but <see cref="RasterizerFactory.ResetForTesting"/> in
    /// other tests' Dispose can remove it. This method re-registers if needed.
    /// </summary>
    private static void EnsureStbTrueTypeRegistered()
    {
        RuntimeHelpers.RunClassConstructor(typeof(StbTrueTypeRasterizer).TypeHandle);

        if (!RasterizerFactory.GetAvailableBackends().Contains(RasterizerBackend.StbTrueType))
            RasterizerFactory.Register(RasterizerBackend.StbTrueType, () => new StbTrueTypeRasterizer());
    }

    public void Dispose()
    {
        _rasterizer?.Dispose();
    }

    // -- 1. Factory registration -----------------------------------------

    [Fact]
    public void Factory_Create_StbTrueType_ReturnsStbTrueTypeRasterizer()
    {
        EnsureStbTrueTypeRegistered();

        using var rasterizer = RasterizerFactory.Create(RasterizerBackend.StbTrueType);

        rasterizer.ShouldNotBeNull();
        rasterizer.ShouldBeOfType<StbTrueTypeRasterizer>();
    }

    // -- 2. Factory reset isolates state ---------------------------------

    [Fact]
    public void Factory_ResetForTesting_RemovesStbTrueTypeRegistration()
    {
        EnsureStbTrueTypeRegistered();

        RasterizerFactory.GetAvailableBackends().ShouldContain(RasterizerBackend.StbTrueType);

        RasterizerFactory.ResetForTesting();

        try
        {
            RasterizerFactory.GetAvailableBackends().ShouldBeEmpty();

            var act = () => RasterizerFactory.Create(RasterizerBackend.StbTrueType);
            Should.Throw<InvalidOperationException>(act);
        }
        finally
        {
            // Re-register all backends to restore state for other tests.
            FreeTypeRegistration.Register();
            StbTrueTypeRegistration.Register();
        }
    }

    // -- 3. LoadFont succeeds --------------------------------------------

    [Fact]
    public void LoadFont_WithValidFont_DoesNotThrow()
    {
        _rasterizer = new StbTrueTypeRasterizer();
        var fontData = LoadTestFont();

        Should.NotThrow(() => _rasterizer.LoadFont(fontData));
    }

    // -- 4. RasterizeGlyph produces output -------------------------------

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

    // -- 5. RasterizeGlyph for missing codepoint -------------------------

    [Fact]
    public void RasterizeGlyph_MissingCodepoint_ReturnsNull()
    {
        _rasterizer = CreateAndLoadRasterizer();

        // StbTrueType returns null for unmapped codepoints (no .notdef fallback).
        var glyph = _rasterizer.RasterizeGlyph(0xFDD0, DefaultOptions);

        glyph.ShouldBeNull();
    }

    // -- 6. RasterizeAll produces multiple glyphs ------------------------

    [Fact]
    public void RasterizeAll_ThreeCodepoints_ReturnsThreeGlyphs()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var codepoints = new[] { 65, 66, 67 }; // A, B, C

        var results = _rasterizer.RasterizeAll(codepoints, DefaultOptions);

        results.Count.ShouldBe(3);
    }

    // -- 7. GetGlyphMetrics returns metrics without bitmap ----------------

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
    public void Capabilities_ReportsStbTrueTypeLimitations()
    {
        _rasterizer = new StbTrueTypeRasterizer();

        var caps = _rasterizer.Capabilities;

        caps.SupportsColorFonts.ShouldBeFalse();
        caps.SupportsVariableFonts.ShouldBeFalse();
        caps.SupportsSdf.ShouldBeTrue();
        caps.SupportsOutlineStroke.ShouldBeFalse();
        caps.SupportsSystemFonts.ShouldBeFalse();
    }

    // -- 9. Pixel format is Grayscale8 -----------------------------------

    [Fact]
    public void RasterizeGlyph_Format_IsGrayscale8()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var glyph = _rasterizer.RasterizeGlyph(65, DefaultOptions);

        glyph.ShouldNotBeNull();
        glyph.Format.ShouldBe(PixelFormat.Grayscale8);
    }

    // -- 10. Dispose releases resources ----------------------------------

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
        var rasterizer = new StbTrueTypeRasterizer();
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

    // -- 13. GetFontMetrics returns valid metrics -------------------------

    [Fact]
    public void GetFontMetrics_ReturnsValidMetrics()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var metrics = _rasterizer.GetFontMetrics(DefaultOptions);

        metrics.ShouldNotBeNull();
        metrics.Ascent.ShouldBeGreaterThan(0);
        metrics.Descent.ShouldBeGreaterThan(0);
        metrics.LineHeight.ShouldBeGreaterThan(0);
    }

    // -- 14. Space character returns empty bitmap with advance ------------

    [Fact]
    public void RasterizeGlyph_SpaceCharacter_ReturnsEmptyBitmapWithAdvance()
    {
        _rasterizer = CreateAndLoadRasterizer();

        var glyph = _rasterizer.RasterizeGlyph(32, DefaultOptions); // space

        glyph.ShouldNotBeNull();
        glyph.Metrics.Advance.ShouldBeGreaterThan(0);
        // Space has no visible bitmap (zero width/height).
        glyph.Width.ShouldBe(0);
        glyph.Height.ShouldBe(0);
    }

    // -- 15. SDF rendering produces distance field bitmap -----------------

    [Fact]
    public void RasterizeGlyph_Sdf_ProducesDistanceFieldBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var sdfOptions = new RasterOptions
        {
            Size = 32,
            AntiAlias = AntiAliasMode.Grayscale,
            Sdf = true
        };

        var glyph = _rasterizer.RasterizeGlyph(65, sdfOptions); // 'A'

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        // SDF bitmaps should contain intermediate values (not just binary 0/255).
        var hasIntermediateValues = glyph.BitmapData.Any(b => b > 0 && b < 255);
        hasIntermediateValues.ShouldBeTrue("SDF bitmap should contain values between 0 and 255");
    }

    // -- 16. LoadSystemFont throws NotSupportedException ------------------

    [Fact]
    public void LoadSystemFont_ThrowsNotSupportedException()
    {
        _rasterizer = new StbTrueTypeRasterizer();
        IRasterizer rasterizer = _rasterizer;

        Should.Throw<NotSupportedException>(
            () => rasterizer.LoadSystemFont("Arial"));
    }

    // -- 17. SuperSample path -----------------------------------------------

    [Fact]
    public void RasterizeGlyph_SuperSample2_ProducesValidBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, SuperSample = 2 };

        var glyph = _rasterizer.RasterizeGlyph(65, options); // 'A'

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        glyph.Metrics.Advance.ShouldBeGreaterThan(0);
    }

    // -- 18. AntiAliasMode.None produces binary bitmap --------------------

    [Fact]
    public void RasterizeGlyph_AntiAliasNone_ProducesBinaryBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, AntiAlias = AntiAliasMode.None };

        var glyph = _rasterizer.RasterizeGlyph(65, options);

        glyph.ShouldNotBeNull();
        glyph.BitmapData.ShouldAllBe(b => b == 0 || b == 255);
    }

    // -- 19. Invalid/corrupt font data ------------------------------------

    [Fact]
    public void LoadFont_InvalidData_ThrowsFontParsingException()
    {
        _rasterizer = new StbTrueTypeRasterizer();
        var garbage = new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };

        Should.Throw<FontParsingException>(() => _rasterizer.LoadFont(garbage));
    }

    // -- 20. Double LoadFont throws ---------------------------------------

    [Fact]
    public void LoadFont_CalledTwice_ThrowsInvalidOperationException()
    {
        _rasterizer = CreateAndLoadRasterizer();

        Should.Throw<InvalidOperationException>(() => _rasterizer.LoadFont(LoadTestFont()));
    }

    // -- 21. Bold support ---------------------------------------------------

    [Fact]
    public void RasterizeGlyph_Bold_ReturnsWiderGlyph()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var normal = new RasterOptions { Size = 32 };
        var bold = new RasterOptions { Size = 32, Bold = true };

        var normalGlyph = _rasterizer.RasterizeGlyph(65, normal);
        var boldGlyph = _rasterizer.RasterizeGlyph(65, bold);

        normalGlyph.ShouldNotBeNull();
        boldGlyph.ShouldNotBeNull();
        boldGlyph.BitmapData.Length.ShouldBeGreaterThan(0);
        // Bold glyph should have more "ink" (higher sum of pixel values) than normal.
        var normalInk = normalGlyph.BitmapData.Sum(b => (long)b);
        var boldInk = boldGlyph.BitmapData.Sum(b => (long)b);
        boldInk.ShouldBeGreaterThan(normalInk);
    }

    // -- 22. Italic support -----------------------------------------------

    [Fact]
    public void RasterizeGlyph_Italic_ReturnsShearedGlyph()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var normal = new RasterOptions { Size = 32 };
        var italic = new RasterOptions { Size = 32, Italic = true };

        var normalGlyph = _rasterizer.RasterizeGlyph(65, normal);
        var italicGlyph = _rasterizer.RasterizeGlyph(65, italic);

        normalGlyph.ShouldNotBeNull();
        italicGlyph.ShouldNotBeNull();
        italicGlyph.BitmapData.Length.ShouldBeGreaterThan(0);
        // Italic shear should produce a wider glyph due to the horizontal shift.
        italicGlyph.Width.ShouldBeGreaterThanOrEqualTo(normalGlyph.Width);
    }

    // -- 23. ColorFont rejection ------------------------------------------

    [Fact]
    public void RasterizeGlyph_ColorFont_ThrowsNotSupportedException()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, ColorFont = true };

        Should.Throw<NotSupportedException>(() => _rasterizer.RasterizeGlyph(65, options));
    }

    // -- 24. SetVariationAxes rejection -----------------------------------

    [Fact]
    public void SetVariationAxes_ThrowsNotSupportedException()
    {
        _rasterizer = CreateAndLoadRasterizer();

        Should.Throw<NotSupportedException>(() =>
            _rasterizer.SetVariationAxes(Array.Empty<VariationAxis>(), new Dictionary<string, float>()));
    }

    // -- 25. SelectColorPalette rejection ---------------------------------

    [Fact]
    public void SelectColorPalette_ThrowsNotSupportedException()
    {
        _rasterizer = CreateAndLoadRasterizer();

        Should.Throw<NotSupportedException>(() => _rasterizer.SelectColorPalette(0));
    }

    // -- 26. Dispose then GetFontMetrics ----------------------------------

    [Fact]
    public void Dispose_ThenGetFontMetrics_ThrowsObjectDisposedException()
    {
        var rasterizer = CreateAndLoadRasterizer();
        rasterizer.Dispose();

        Should.Throw<ObjectDisposedException>(
            () => rasterizer.GetFontMetrics(DefaultOptions));
    }

    // -- 27. Dispose idempotency ------------------------------------------

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var rasterizer = CreateAndLoadRasterizer();
        rasterizer.Dispose();

        Should.NotThrow(() => rasterizer.Dispose());
    }

    // -- 28. End-to-end BmFont.Generate with StbTrueType ------------------

    [Fact]
    public void BmFont_Generate_WithStbTrueType_ProducesValidOutput()
    {
        EnsureStbTrueTypeRegistered();

        var result = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.StbTrueType
        });

        result.ShouldNotBeNull();
        result.FntText.ShouldNotBeNullOrEmpty();
        result.Pages.Count.ShouldBeGreaterThan(0);
    }

    // -- 29. Bold + Italic combined ----------------------------------------

    [Fact]
    public void RasterizeGlyph_BoldAndItalic_ProducesValidBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Bold = true, Italic = true };

        var glyph = _rasterizer.RasterizeGlyph(65, options); // 'A'

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        glyph.BitmapData.Length.ShouldBeGreaterThan(0);
    }

    // -- 30. SDF + Bold ----------------------------------------------------

    [Fact]
    public void RasterizeGlyph_SdfBold_ProducesValidBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Sdf = true, Bold = true };

        var glyph = _rasterizer.RasterizeGlyph(65, options); // 'A'

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        glyph.BitmapData.Length.ShouldBeGreaterThan(0);
    }

    // -- 31. SDF + Italic --------------------------------------------------

    [Fact]
    public void RasterizeGlyph_SdfItalic_ProducesValidBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Sdf = true, Italic = true };

        var glyph = _rasterizer.RasterizeGlyph(65, options); // 'A'

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        glyph.BitmapData.Length.ShouldBeGreaterThan(0);
    }

    // -- 32. SDF + Bold + Italic -------------------------------------------

    [Fact]
    public void RasterizeGlyph_SdfBoldItalic_ProducesValidBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Sdf = true, Bold = true, Italic = true };

        var glyph = _rasterizer.RasterizeGlyph(65, options); // 'A'

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        glyph.BitmapData.Length.ShouldBeGreaterThan(0);
    }

    // -- 33. Bold metrics: advance >= normal --------------------------------

    [Fact]
    public void RasterizeGlyph_Bold_AdvanceGreaterOrEqualToNormal()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var normal = new RasterOptions { Size = 32 };
        var bold = new RasterOptions { Size = 32, Bold = true };

        var normalGlyph = _rasterizer.RasterizeGlyph(65, normal);
        var boldGlyph = _rasterizer.RasterizeGlyph(65, bold);

        normalGlyph.ShouldNotBeNull();
        boldGlyph.ShouldNotBeNull();
        boldGlyph.Metrics.Advance.ShouldBeGreaterThanOrEqualTo(normalGlyph.Metrics.Advance);
    }

    // -- 34. Italic metrics: advance roughly equals normal ------------------

    [Fact]
    public void RasterizeGlyph_Italic_AdvanceRoughlyEqualsNormal()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var normal = new RasterOptions { Size = 32 };
        var italic = new RasterOptions { Size = 32, Italic = true };

        var normalGlyph = _rasterizer.RasterizeGlyph(65, normal);
        var italicGlyph = _rasterizer.RasterizeGlyph(65, italic);

        normalGlyph.ShouldNotBeNull();
        italicGlyph.ShouldNotBeNull();
        // Italic should not significantly change the advance width.
        var diff = Math.Abs(italicGlyph.Metrics.Advance - normalGlyph.Metrics.Advance);
        diff.ShouldBeLessThanOrEqualTo(2, "Italic should not significantly change advance width");
    }

    // -- 35. GetGlyphMetrics with Bold --------------------------------------

    [Fact]
    public void GetGlyphMetrics_Bold_ReturnsValidMetrics()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Bold = true };

        var metrics = _rasterizer.GetGlyphMetrics(65, options); // 'A'

        metrics.ShouldNotBeNull();
        metrics.Value.Advance.ShouldBeGreaterThan(0);
    }

    // -- 36. GetGlyphMetrics with Italic ------------------------------------

    [Fact]
    public void GetGlyphMetrics_Italic_ReturnsValidMetrics()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Italic = true };

        var metrics = _rasterizer.GetGlyphMetrics(65, options); // 'A'

        metrics.ShouldNotBeNull();
        metrics.Value.Advance.ShouldBeGreaterThan(0);
    }

    // -- 37. Whitespace glyph bold ------------------------------------------

    [Fact]
    public void RasterizeGlyph_SpaceBold_ReturnsEmptyBitmapWithAdvance()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Bold = true };

        var glyph = _rasterizer.RasterizeGlyph(32, options); // space

        glyph.ShouldNotBeNull();
        glyph.Metrics.Advance.ShouldBeGreaterThan(0);
        glyph.Width.ShouldBe(0);
        glyph.Height.ShouldBe(0);
    }

    // -- 38. Whitespace glyph italic ----------------------------------------

    [Fact]
    public void RasterizeGlyph_SpaceItalic_ReturnsEmptyBitmapWithAdvance()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Italic = true };

        var glyph = _rasterizer.RasterizeGlyph(32, options); // space

        glyph.ShouldNotBeNull();
        glyph.Metrics.Advance.ShouldBeGreaterThan(0);
        glyph.Width.ShouldBe(0);
        glyph.Height.ShouldBe(0);
    }

    // -- 39. Non-styled fast path regression guard --------------------------

    [Fact]
    public void RasterizeGlyph_NoStyle_StillProducesValidBitmap()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32 };

        var glyph = _rasterizer.RasterizeGlyph(72, options); // 'H'

        glyph.ShouldNotBeNull();
        glyph.Width.ShouldBeGreaterThan(0);
        glyph.Height.ShouldBeGreaterThan(0);
        glyph.BitmapData.Length.ShouldBeGreaterThan(0);
        glyph.Metrics.Advance.ShouldBeGreaterThan(0);
    }

    // -- 40. GetGlyphMetrics with SDF + Bold --------------------------------

    [Fact]
    public void GetGlyphMetrics_SdfBold_ReturnsValidMetrics()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Sdf = true, Bold = true };

        var metrics = _rasterizer.GetGlyphMetrics(65, options); // 'A'

        metrics.ShouldNotBeNull();
        metrics.Value.Advance.ShouldBeGreaterThan(0);
        metrics.Value.Width.ShouldBeGreaterThan(0);
        metrics.Value.Height.ShouldBeGreaterThan(0);
    }

    // -- 41. GetGlyphMetrics matches RasterizeGlyph for Bold -----------------

    [Fact]
    public void GetGlyphMetrics_MatchesRasterizedGlyph_ForBold()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Bold = true };

        var metrics = _rasterizer.GetGlyphMetrics(65, options); // 'A'
        var glyph = _rasterizer.RasterizeGlyph(65, options);

        metrics.ShouldNotBeNull();
        glyph.ShouldNotBeNull();

        // Advance should match exactly.
        metrics.Value.Advance.ShouldBe(glyph.Metrics.Advance);
        // Width and Height should be equal or very close (rounding differences).
        Math.Abs(metrics.Value.Width - glyph.Metrics.Width).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(metrics.Value.Height - glyph.Metrics.Height).ShouldBeLessThanOrEqualTo(1);
        // BearingX and BearingY should also be very close.
        Math.Abs(metrics.Value.BearingX - glyph.Metrics.BearingX).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(metrics.Value.BearingY - glyph.Metrics.BearingY).ShouldBeLessThanOrEqualTo(1);
    }

    // -- 42. GetGlyphMetrics matches RasterizeGlyph for SDF + Bold ----------

    [Fact]
    public void GetGlyphMetrics_MatchesRasterizedGlyph_ForSdfBold()
    {
        _rasterizer = CreateAndLoadRasterizer();
        var options = new RasterOptions { Size = 32, Sdf = true, Bold = true };

        var metrics = _rasterizer.GetGlyphMetrics(65, options); // 'A'
        var glyph = _rasterizer.RasterizeGlyph(65, options);

        metrics.ShouldNotBeNull();
        glyph.ShouldNotBeNull();

        // Advance should match exactly.
        metrics.Value.Advance.ShouldBe(glyph.Metrics.Advance);
        // SDF adds 5px padding on each side (10px total), plus bold expansion and rounding.
        // Use a tolerance of 12 to account for all these combined effects.
        const int sdfTolerance = 12;
        Math.Abs(metrics.Value.Width - glyph.Metrics.Width).ShouldBeLessThanOrEqualTo(sdfTolerance);
        Math.Abs(metrics.Value.Height - glyph.Metrics.Height).ShouldBeLessThanOrEqualTo(sdfTolerance);
        // BearingX and BearingY also affected by SDF padding.
        Math.Abs(metrics.Value.BearingX - glyph.Metrics.BearingX).ShouldBeLessThanOrEqualTo(sdfTolerance);
        Math.Abs(metrics.Value.BearingY - glyph.Metrics.BearingY).ShouldBeLessThanOrEqualTo(sdfTolerance);
    }

    // -- Helpers ----------------------------------------------------------

    private static StbTrueTypeRasterizer CreateAndLoadRasterizer()
    {
        var rasterizer = new StbTrueTypeRasterizer();
        rasterizer.LoadFont(LoadTestFont());
        return rasterizer;
    }
}
