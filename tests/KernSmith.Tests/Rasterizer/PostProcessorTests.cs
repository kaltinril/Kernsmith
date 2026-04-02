using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Tests for BoldPostProcessor and ItalicPostProcessor bitmap-level fallbacks.
/// </summary>
public class PostProcessorTests
{
    /// <summary>Creates a synthetic grayscale glyph for testing.</summary>
    private static RasterizedGlyph MakeGlyph(int width, int height, PixelFormat format = PixelFormat.Grayscale8)
    {
        var bpp = format == PixelFormat.Rgba32 ? 4 : 1;
        var pitch = width * bpp;
        var data = new byte[pitch * height];

        // Fill with a solid rectangle of non-zero values to simulate rendered content.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (bpp == 1)
                {
                    data[y * pitch + x] = 200;
                }
                else
                {
                    var idx = y * pitch + x * 4;
                    data[idx] = 255;     // R
                    data[idx + 1] = 128; // G
                    data[idx + 2] = 64;  // B
                    data[idx + 3] = 200; // A
                }
            }
        }

        return new RasterizedGlyph
        {
            Codepoint = 65,
            GlyphIndex = 1,
            BitmapData = data,
            Width = width,
            Height = height,
            Pitch = pitch,
            Metrics = new GlyphMetrics(BearingX: 2, BearingY: 10, Advance: 12, Width: width, Height: height),
            Format = format
        };
    }

    /// <summary>Creates an empty (zero-size) glyph, like a whitespace character.</summary>
    private static RasterizedGlyph MakeEmptyGlyph(PixelFormat format = PixelFormat.Grayscale8)
    {
        return new RasterizedGlyph
        {
            Codepoint = 32,
            GlyphIndex = 3,
            BitmapData = Array.Empty<byte>(),
            Width = 0,
            Height = 0,
            Pitch = 0,
            Metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: 8, Width: 0, Height: 0),
            Format = format
        };
    }

    // == BoldPostProcessor ==================================================

    // -- 1. Output is wider/taller than input --------------------------------

    [Fact]
    public void BoldPostProcessor_Process_OutputIsWiderAndTaller()
    {
        var processor = new BoldPostProcessor(strength: 1);
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Width.ShouldBeGreaterThan(input.Width);
        output.Height.ShouldBeGreaterThan(input.Height);
    }

    // -- 2. Metrics: BearingX decreases, Width increases ---------------------

    [Fact]
    public void BoldPostProcessor_Process_MetricsBearingXDecreasesWidthIncreases()
    {
        var processor = new BoldPostProcessor(strength: 1);
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Metrics.BearingX.ShouldBeLessThan(input.Metrics.BearingX);
        output.Metrics.Width.ShouldBeGreaterThan(input.Metrics.Width);
        output.Metrics.Advance.ShouldBeGreaterThan(input.Metrics.Advance);
    }

    // -- 3. Higher strength produces wider output ----------------------------

    [Fact]
    public void BoldPostProcessor_HigherStrength_ProducesWiderOutput()
    {
        var weak = new BoldPostProcessor(strength: 1);
        var strong = new BoldPostProcessor(strength: 3);
        var input = MakeGlyph(10, 12);

        var weakOutput = weak.Process(input);
        var strongOutput = strong.Process(input);

        strongOutput.Width.ShouldBeGreaterThan(weakOutput.Width);
        strongOutput.Height.ShouldBeGreaterThan(weakOutput.Height);
    }

    // -- 4. Empty glyph returns zero-size output (no crash) ------------------

    [Fact]
    public void BoldPostProcessor_EmptyGlyph_ReturnsZeroSizeOutput()
    {
        var processor = new BoldPostProcessor(strength: 1);
        var input = MakeEmptyGlyph();

        var output = processor.Process(input);

        output.Width.ShouldBe(0);
        output.Height.ShouldBe(0);
        output.BitmapData.Length.ShouldBe(0);
    }

    // -- 5. RGBA format works ------------------------------------------------

    [Fact]
    public void BoldPostProcessor_RgbaGlyph_ProducesValidOutput()
    {
        var processor = new BoldPostProcessor(strength: 1);
        var input = MakeGlyph(10, 12, PixelFormat.Rgba32);

        var output = processor.Process(input);

        output.Width.ShouldBeGreaterThan(input.Width);
        output.Height.ShouldBeGreaterThan(input.Height);
        output.Format.ShouldBe(PixelFormat.Rgba32);
        // RGBA output should have 4 bytes per pixel.
        output.BitmapData.Length.ShouldBe(output.Width * output.Height * 4);
    }

    // == ItalicPostProcessor ================================================

    // -- 6. Output is wider than input ---------------------------------------

    [Fact]
    public void ItalicPostProcessor_Process_OutputIsWiderThanInput()
    {
        var processor = new ItalicPostProcessor();
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Width.ShouldBeGreaterThan(input.Width);
        // Height should remain the same (shear does not add rows).
        output.Height.ShouldBe(input.Height);
    }

    // -- 7. Metrics: Width increases from shear ------------------------------

    [Fact]
    public void ItalicPostProcessor_Process_MetricsWidthIncreases()
    {
        var processor = new ItalicPostProcessor();
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Metrics.Width.ShouldBeGreaterThan(input.Metrics.Width);
    }

    // -- 8. Default shear matches FreeType (0.2126f) -------------------------

    [Fact]
    public void ItalicPostProcessor_DefaultShear_MatchesFreeType()
    {
        var processor = new ItalicPostProcessor();

        processor.ShearFactor.ShouldBe(0.2126f);
    }

    // -- 9. Empty glyph handled gracefully -----------------------------------

    [Fact]
    public void ItalicPostProcessor_EmptyGlyph_ReturnsZeroSizeOutput()
    {
        var processor = new ItalicPostProcessor();
        var input = MakeEmptyGlyph();

        var output = processor.Process(input);

        output.Width.ShouldBe(0);
        output.Height.ShouldBe(0);
        output.BitmapData.Length.ShouldBe(0);
    }

    // -- 10. RGBA format works -----------------------------------------------

    [Fact]
    public void ItalicPostProcessor_RgbaGlyph_ProducesValidOutput()
    {
        var processor = new ItalicPostProcessor();
        var input = MakeGlyph(10, 12, PixelFormat.Rgba32);

        var output = processor.Process(input);

        output.Width.ShouldBeGreaterThan(input.Width);
        output.Format.ShouldBe(PixelFormat.Rgba32);
        output.BitmapData.Length.ShouldBe(output.Width * output.Height * 4);
    }
}
