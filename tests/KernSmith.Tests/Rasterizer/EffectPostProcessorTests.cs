using KernSmith.Atlas;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Isolated unit tests for the effect post-processors (outline, shadow, gradient, height
/// stretch). BoldPostProcessor and ItalicPostProcessor are covered by PostProcessorTests.
/// </summary>
public class EffectPostProcessorTests
{
    /// <summary>Creates a synthetic glyph with a solid filled rectangle of content.</summary>
    private static RasterizedGlyph MakeGlyph(int width, int height, PixelFormat format = PixelFormat.Grayscale8)
    {
        var bpp = format == PixelFormat.Rgba32 ? 4 : 1;
        var pitch = width * bpp;
        var data = new byte[pitch * height];

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
                    data[idx] = 255;
                    data[idx + 1] = 128;
                    data[idx + 2] = 64;
                    data[idx + 3] = 200;
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

    // == OutlinePostProcessor ===============================================

    [Fact]
    public void OutlinePostProcessor_Process_GrowsByTwiceWidthOnEachAxis()
    {
        var processor = new OutlinePostProcessor(outlineWidth: 2);
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Width.ShouldBe(input.Width + 4);
        output.Height.ShouldBe(input.Height + 4);
    }

    [Fact]
    public void OutlinePostProcessor_Process_ProducesRgbaOutput()
    {
        var processor = new OutlinePostProcessor(outlineWidth: 1);
        var input = MakeGlyph(8, 8);

        var output = processor.Process(input);

        output.Format.ShouldBe(PixelFormat.Rgba32);
        output.BitmapData.Length.ShouldBe(output.Width * output.Height * 4);
    }

    [Fact]
    public void OutlinePostProcessor_Process_OutlineColorAffectsOutput()
    {
        var input = MakeGlyph(8, 8);

        var blue = new OutlinePostProcessor(outlineWidth: 2, outlineR: 0, outlineG: 0, outlineB: 255).Process(input);
        var green = new OutlinePostProcessor(outlineWidth: 2, outlineR: 0, outlineG: 255, outlineB: 0).Process(input);

        // Same geometry, different outline color, so the pixel data must differ.
        blue.Width.ShouldBe(green.Width);
        blue.Height.ShouldBe(green.Height);
        blue.BitmapData.ShouldNotBe(green.BitmapData);

        // The blue-outline output must contain a pixel with a strong blue channel from the outline.
        var foundBlue = false;
        for (var i = 0; i + 3 < blue.BitmapData.Length; i += 4)
        {
            if (blue.BitmapData[i + 3] > 0 && blue.BitmapData[i + 2] > blue.BitmapData[i])
            {
                foundBlue = true;
                break;
            }
        }

        foundBlue.ShouldBeTrue();
    }

    [Fact]
    public void OutlinePostProcessor_ZeroWidth_ReturnsInputUnchanged()
    {
        var processor = new OutlinePostProcessor(outlineWidth: 0);
        var input = MakeGlyph(8, 8);

        var output = processor.Process(input);

        output.ShouldBeSameAs(input);
    }

    [Fact]
    public void OutlinePostProcessor_EmptyGlyph_ReturnsInputUnchanged()
    {
        var processor = new OutlinePostProcessor(outlineWidth: 2);
        var input = MakeEmptyGlyph();

        var output = processor.Process(input);

        output.ShouldBeSameAs(input);
    }

    // == ShadowPostProcessor ================================================

    [Fact]
    public void ShadowPostProcessor_Process_ExpandsCanvasForOffset()
    {
        var processor = new ShadowPostProcessor(offsetX: 3, offsetY: 3);
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Width.ShouldBeGreaterThan(input.Width);
        output.Height.ShouldBeGreaterThan(input.Height);
        output.Format.ShouldBe(PixelFormat.Rgba32);
    }

    [Fact]
    public void ShadowPostProcessor_Process_UsesShadowColor()
    {
        var processor = new ShadowPostProcessor(offsetX: 4, offsetY: 4, shadowR: 200, shadowG: 0, shadowB: 0);
        var input = MakeGlyph(8, 8);

        var output = processor.Process(input);

        // Somewhere in the output there must be a red-tinted shadow pixel.
        var foundRedShadow = false;
        for (var i = 0; i + 3 < output.BitmapData.Length; i += 4)
        {
            if (output.BitmapData[i] > 100 && output.BitmapData[i + 3] > 0)
            {
                foundRedShadow = true;
                break;
            }
        }

        foundRedShadow.ShouldBeTrue();
    }

    [Fact]
    public void ShadowPostProcessor_ClampsOpacityToUnitRange()
    {
        new ShadowPostProcessor(opacity: 5f).Opacity.ShouldBe(1f);
        new ShadowPostProcessor(opacity: -1f).Opacity.ShouldBe(0f);
    }

    // == GradientPostProcessor ==============================================

    [Fact]
    public void GradientPostProcessor_Process_PreservesDimensionsAndProducesRgba()
    {
        var processor = new GradientPostProcessor(255, 0, 0, 0, 0, 255);
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Width.ShouldBe(input.Width);
        output.Height.ShouldBe(input.Height);
        output.Format.ShouldBe(PixelFormat.Rgba32);
    }

    [Fact]
    public void GradientPostProcessor_Process_TopAndBottomDifferForVerticalGradient()
    {
        // 90 degrees = top-to-bottom: start color at top, end color at bottom.
        var processor = new GradientPostProcessor(255, 0, 0, 0, 0, 255, angleDegrees: 90f);
        var input = MakeGlyph(8, 8);

        var output = processor.Process(input);

        var topR = output.BitmapData[0];
        var bottomRowStart = (output.Height - 1) * output.Pitch;
        var bottomR = output.BitmapData[bottomRowStart];

        // The red channel should be stronger at the top than at the bottom.
        topR.ShouldBeGreaterThan(bottomR);
    }

    [Fact]
    public void GradientPostProcessor_ClampsMidpoint()
    {
        new GradientPostProcessor(0, 0, 0, 0, 0, 0, midpoint: 5f).Midpoint.ShouldBe(0.99f);
        new GradientPostProcessor(0, 0, 0, 0, 0, 0, midpoint: -5f).Midpoint.ShouldBe(0.01f);
    }

    // == HeightStretchPostProcessor =========================================

    [Fact]
    public void HeightStretchPostProcessor_TallerPercent_IncreasesHeight()
    {
        var processor = new HeightStretchPostProcessor(200);
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Height.ShouldBe(24);
        output.Width.ShouldBe(input.Width);
    }

    [Fact]
    public void HeightStretchPostProcessor_ShorterPercent_DecreasesHeight()
    {
        var processor = new HeightStretchPostProcessor(50);
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.Height.ShouldBe(6);
    }

    [Fact]
    public void HeightStretchPostProcessor_HundredPercent_ReturnsInputUnchanged()
    {
        var processor = new HeightStretchPostProcessor(100);
        var input = MakeGlyph(10, 12);

        var output = processor.Process(input);

        output.ShouldBeSameAs(input);
    }

    [Fact]
    public void HeightStretchPostProcessor_ClampsMinimumPercent()
    {
        new HeightStretchPostProcessor(1).HeightPercent.ShouldBe(10);
    }

    [Fact]
    public void HeightStretchPostProcessor_RgbaGlyph_PreservesFormat()
    {
        var processor = new HeightStretchPostProcessor(150);
        var input = MakeGlyph(8, 8, PixelFormat.Rgba32);

        var output = processor.Process(input);

        output.Format.ShouldBe(PixelFormat.Rgba32);
        output.BitmapData.Length.ShouldBe(output.Width * output.Height * 4);
    }
}
