using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 173 — Native's LCD subpixel renderer: 3x horizontal coverage, a low-pass filter, and
/// RGBA output with one subpixel per color channel.
/// </summary>
public class LcdRendererTests
{
    private const string GlyphSample = "ABHIOSWaegilmosw0158.,!?@&";

    public static TheoryData<string, string> Variants => new()
    {
        { "PixelBox", "Rgb" },
        { "PixelBox", "Bgr" },
        { "Triangle", "Rgb" },
        { "Triangle", "Bgr" },
    };

    private static LcdRenderer Renderer(string filter, string order) => new(
        filter == "PixelBox" ? LcdFilter.PixelBox : LcdFilter.Triangle,
        order == "Rgb" ? LcdSubpixelOrder.Rgb : LcdSubpixelOrder.Bgr);

    // ---- Synthetic rectangles ----------------------------------------------------------------

    [Theory]
    [InlineData("PixelBox", "Rgb", 2.0f, 5.0f)]
    [InlineData("PixelBox", "Rgb", 2.25f, 4.6f)]
    [InlineData("PixelBox", "Rgb", 1.5f, 3.1f)]
    [InlineData("PixelBox", "Rgb", 2.4f, 2.9f)]
    [InlineData("PixelBox", "Bgr", 2.25f, 4.6f)]
    [InlineData("Triangle", "Rgb", 2.0f, 5.0f)]
    [InlineData("Triangle", "Rgb", 2.25f, 4.6f)]
    [InlineData("Triangle", "Rgb", 2.4f, 2.9f)]
    [InlineData("Triangle", "Bgr", 1.5f, 3.1f)]
    public void Rectangle_AtSubpixelOffsets_MatchesTheAnalyticFilteredCoverage(
        string filterName, string orderName, float left, float right)
    {
        var renderer = Renderer(filterName, orderName);
        var filter = filterName == "PixelBox" ? LcdFilter.PixelBox : LcdFilter.Triangle;
        var glyph = Rectangle(left, right, bottom: 0f, top: 4f);

        var bitmap = renderer.Render('x', 1, glyph, Options);

        bitmap.Format.ShouldBe(PixelFormat.Rgba32);
        bitmap.Pitch.ShouldBe(bitmap.Width * 4);
        bitmap.Height.ShouldBe(4);

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                int pixel = bitmap.Metrics.BearingX + x;
                int i = (y * bitmap.Pitch) + (x * 4);
                byte[] rgb = [bitmap.BitmapData[i], bitmap.BitmapData[i + 1], bitmap.BitmapData[i + 2]];
                if (orderName == "Bgr")
                    (rgb[0], rgb[2]) = (rgb[2], rgb[0]);

                for (int c = 0; c < 3; c++)
                {
                    double expected = FilteredCoverage(filter, left, right, (3 * pixel) + c);
                    Math.Abs(rgb[c] - expected).ShouldBeLessThanOrEqualTo(2,
                        $"pixel {pixel} row {y} subpixel {c}: expected {expected:F1}, got {rgb[c]}");
                }

                bitmap.BitmapData[i + 3].ShouldBe(rgb.Max(), "alpha is the strongest subpixel");
            }
        }

        // The whole filtered footprint is inside the bitmap: every subpixel just outside it is 0.
        double outsideLeft = FilteredCoverage(filter, left, right, (3 * bitmap.Metrics.BearingX) - 1);
        double outsideRight = FilteredCoverage(filter, left, right, 3 * (bitmap.Metrics.BearingX + bitmap.Width));
        outsideLeft.ShouldBeLessThan(0.5);
        outsideRight.ShouldBeLessThan(0.5);
    }

    [Fact]
    public void HorizontalEdges_HaveNoColorFringe()
    {
        // A wide bar with fractional top and bottom: away from its vertical edges each pixel's
        // three subpixels see the same coverage, so the partially covered rows stay neutral.
        var glyph = Rectangle(1f, 12f, bottom: 0.3f, top: 3.6f);
        var bitmap = LcdRenderer.Instance.Render('x', 1, glyph, Options);

        bool sawPartialRow = false;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int pixel = 3; pixel <= 9; pixel++)
            {
                int i = (y * bitmap.Pitch) + ((pixel - bitmap.Metrics.BearingX) * 4);
                byte r = bitmap.BitmapData[i], g = bitmap.BitmapData[i + 1], b = bitmap.BitmapData[i + 2];
                g.ShouldBe(r, $"row {y} pixel {pixel}");
                b.ShouldBe(r, $"row {y} pixel {pixel}");
                if (r is > 0 and < 255)
                    sawPartialRow = true;
            }
        }

        sawPartialRow.ShouldBeTrue("the bar must have partially covered rows for this test to mean anything");
    }

    // ---- Real glyphs ------------------------------------------------------------------------

    [Theory]
    [InlineData(12f)]
    [InlineData(14f)]
    [InlineData(16f)]
    public void LowercaseL_HasThreeDistinctChannelCoverages(float size)
    {
        using var rasterizer = LoadedRoboto();
        var glyph = rasterizer.RasterizeGlyph('l', new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Lcd });

        glyph.ShouldNotBeNull();
        glyph!.Format.ShouldBe(PixelFormat.Rgba32);
        Pixels(glyph).ShouldContain(p => p.R != p.G && p.G != p.B && p.R != p.B,
            $"'l' at {size}px has no pixel whose three subpixels differ — this is grayscale, not LCD");
    }

    [Theory]
    [InlineData(12f)]
    [InlineData(16f)]
    [InlineData(32f)]
    public void Bgr_IsRgbWithRedAndBlueSwapped(float size)
    {
        var bytes = TestFonts.RobotoRegularBytes();
        using var face = new NativeRasterizer();
        face.LoadFont(bytes);

        foreach (char character in GlyphSample)
        {
            var prepared = PrepareGlyph(face, character, size);
            var rgb = new LcdRenderer(LcdFilter.PixelBox, LcdSubpixelOrder.Rgb).Render(character, 1, prepared, Options);
            var bgr = new LcdRenderer(LcdFilter.PixelBox, LcdSubpixelOrder.Bgr).Render(character, 1, prepared, Options);

            bgr.Metrics.ShouldBe(rgb.Metrics);
            var expected = (byte[])rgb.BitmapData.Clone();
            for (int i = 0; i < expected.Length; i += 4)
                (expected[i], expected[i + 2]) = (expected[i + 2], expected[i]);
            bgr.BitmapData.ShouldBe(expected, $"'{character}'");
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Measure_EqualsWhatRenderProduces(string filter, string order)
    {
        var renderer = Renderer(filter, order);
        using var rasterizer = LoadedRoboto();

        foreach (float size in new[] { 8f, 12f, 16f, 32f, 72f })
        {
            for (int codepoint = 0x21; codepoint < 0x7F; codepoint++)
            {
                var prepared = PrepareGlyph(rasterizer, codepoint, size);
                var rendered = renderer.Render(codepoint, 1, prepared, Options);
                var measured = renderer.Measure(prepared, Options);

                measured.ShouldBe(rendered.Metrics, $"U+{codepoint:X4} at {size}px");
                rendered.Width.ShouldBe(measured.Width);
                rendered.Height.ShouldBe(measured.Height);
                rendered.BitmapData.Length.ShouldBe(rendered.Pitch * rendered.Height);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void NoInkIsLost_SubpixelEnergyMatchesGrayscaleCoverage(string filter, string order)
    {
        // The filter's taps sum to one, so if nothing is clipped the subpixel values add up to
        // three times the grayscale coverage of the same glyph (each pixel is three subpixels).
        // A box too narrow on either side would drop the filter's spill and fall short.
        var renderer = Renderer(filter, order);
        using var rasterizer = LoadedRoboto();
        var failures = new StringBuilder();

        foreach (float size in new[] { 9f, 16f, 40f })
        {
            foreach (char character in GlyphSample)
            {
                var prepared = PrepareGlyph(rasterizer, character, size);
                var lcd = renderer.Render(character, 1, prepared, Options);

                // Grayscale coverage of the same flattened polygon in the same box, so the only
                // way the totals can part is ink the LCD bitmap's columns fail to hold.
                var box = prepared.Box;
                var edges = OutlineFlattener.Flatten(prepared.Outline, prepared.ToPixels, OutlineFlattener.DefaultTolerance / 3f);
                var gray = ScanlineRasterizer.Rasterize(edges, box.Width, box.Height, box.MinX, box.MinY).Bitmap;

                long lcdEnergy = Pixels(lcd).Sum(p => (long)p.R + p.G + p.B);
                long grayEnergy = 3L * gray.Sum(v => (long)v);

                // Every stored value is rounded or truncated independently: allow a couple of
                // levels per subpixel touched.
                long tolerance = 2L * lcd.Width * lcd.Height * 3;
                if (Math.Abs(lcdEnergy - grayEnergy) > tolerance)
                {
                    failures.Append(CultureInfo.InvariantCulture,
                        $"'{character}' {size}px: lcd {lcdEnergy}, 3x gray {grayEnergy}\n");
                }
            }
        }

        failures.ToString().ShouldBeEmpty();
    }

    // ---- Through the rasterizer ---------------------------------------------------------------

    [Fact]
    public void Rasterizer_RendersLcdAsRgbaWithMetricsMatchingGetGlyphMetrics()
    {
        using var rasterizer = LoadedRoboto();
        var options = new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.Lcd };

        foreach (char character in GlyphSample)
        {
            var glyph = rasterizer.RasterizeGlyph(character, options);
            glyph.ShouldNotBeNull();
            glyph!.Format.ShouldBe(PixelFormat.Rgba32);
            rasterizer.GetGlyphMetrics(character, options).ShouldBe(glyph.Metrics, $"'{character}'");
        }

        // Blank glyphs keep their existing shape.
        var space = rasterizer.RasterizeGlyph(' ', options);
        space.ShouldNotBeNull();
        space!.Width.ShouldBe(0);
    }

    [Fact]
    public void Rasterizer_LightAndGrayscale_StayGrayscaleCoverage()
    {
        // Native has no light hinting yet (Phase 174), so a direct Light request is the
        // grayscale coverage render, byte for byte; core negotiation degrades it the same way.
        using var rasterizer = LoadedRoboto();
        foreach (char character in GlyphSample)
        {
            var light = rasterizer.RasterizeGlyph(character, new RasterOptions { Size = 20, AntiAlias = AntiAliasMode.Light })!;
            var gray = rasterizer.RasterizeGlyph(character, new RasterOptions { Size = 20, AntiAlias = AntiAliasMode.Grayscale })!;
            light.Format.ShouldBe(PixelFormat.Grayscale8);
            light.BitmapData.ShouldBe(gray.BitmapData);
            light.Metrics.ShouldBe(gray.Metrics);
        }
    }

    [Fact]
    public void Generate_WithLcd_ProducesAnRgbAtlasThatIsNotGrayscale()
    {
        var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), new FontGeneratorOptions
        {
            Size = 16,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.Native,
            AntiAlias = AntiAliasMode.Lcd,
        });

        var page = result.Pages[0];
        page.Format.ShouldBe(PixelFormat.Rgba32);
        bool colored = false;
        for (int i = 0; i < page.PixelData.Length; i += 4)
        {
            if (page.PixelData[i] != page.PixelData[i + 1] || page.PixelData[i + 1] != page.PixelData[i + 2])
            {
                colored = true;
                break;
            }
        }

        colored.ShouldBeTrue("an LCD atlas must carry distinct subpixel channels");
        result.FntText.ShouldContain("smooth=1", Case.Sensitive);
    }

    // ---- Against FreeType ---------------------------------------------------------------------

    [Theory]
    [InlineData(12f)]
    [InlineData(16f)]
    [InlineData(24f)]
    [InlineData(32f)]
    public void MatchesFreeTypeLcd_PerChannel(float size)
    {
        // The FreeType backend renders FT_RENDER_MODE_LCD without selecting a filter, and its
        // output measures as the pixel-box filter: Native's default scores 0.98-1.00 per glyph
        // and channel, so the bar sits well above Phase 179's 0.85. Unhinted on both sides:
        // Native has no hinter, so a hinted FreeType would measure hinting, not subpixels.
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Lcd, EnableHinting = false };
        var bytes = TestFonts.RobotoRegularBytes();
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);

        var report = new StringBuilder();
        double[] totals = new double[3];
        int count = 0;

        foreach (char character in GlyphSample)
        {
            var mine = native.RasterizeGlyph(character, options)!;
            var reference = freeType.RasterizeGlyph(character, options)!;

            for (int c = 0; c < 3; c++)
            {
                var (a, b, w, h) = OnCommonCanvas(NativeChannel(mine, c), FreeTypeChannel(reference, c));
                double ssim = ImageSimilarity.Ssim(a, b, w, h);
                totals[c] += ssim;
                report.Append(CultureInfo.InvariantCulture, $"'{character}'[{"RGB"[c]}]={ssim:F3} ");
            }

            count++;
        }

        for (int c = 0; c < 3; c++)
        {
            double mean = totals[c] / count;
            mean.ShouldBeGreaterThan(0.99, $"channel {"RGB"[c]} at {size}px: mean SSIM {mean:F3}\n{report}");
        }
    }

    // ---- CFF outlines -------------------------------------------------------------------------

    [Theory]
    [InlineData("serif", 12f)]
    [InlineData("serif", 16f)]
    [InlineData("code", 16f)]
    [InlineData("code", 24f)]
    public void CffGlyphs_RenderLcd_WithDistinctChannels_AndMeasureWhatTheyRender(string font, float size)
    {
        // CFF and CFF2 outlines (Phase 166) reach the LCD renderer like glyf ones.
        using var native = new NativeRasterizer();
        native.LoadFont(font == "serif" ? TestFonts.SourceSerifOtfBytes() : TestFonts.SourceCodeVariableOtfBytes());
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Lcd };

        foreach (char character in GlyphSample)
        {
            var glyph = native.RasterizeGlyph(character, options);
            glyph.ShouldNotBeNull();
            glyph!.Format.ShouldBe(PixelFormat.Rgba32, $"'{character}'");
            native.GetGlyphMetrics(character, options).ShouldBe(glyph.Metrics, $"'{character}'");
        }

        var l = native.RasterizeGlyph('l', options)!;
        Pixels(l).ShouldContain(p => p.R != p.G && p.G != p.B && p.R != p.B,
            $"CFF 'l' at {size}px has no pixel whose three subpixels differ — this is grayscale, not LCD");
    }

    [Theory]
    [InlineData(16f)]
    [InlineData(32f)]
    public void CffGlyphs_MatchFreeTypeLcd_PerChannel(float size)
    {
        // Source Serif 4 scores 0.994 (16px) and 0.998 (32px) mean per channel; unhinted both sides.
        var options = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Lcd, EnableHinting = false };
        var bytes = TestFonts.SourceSerifOtfBytes();
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);

        var report = new StringBuilder();
        double[] totals = new double[3];
        foreach (char character in GlyphSample)
        {
            var mine = native.RasterizeGlyph(character, options)!;
            var reference = freeType.RasterizeGlyph(character, options)!;
            for (int c = 0; c < 3; c++)
            {
                var (a, b, w, h) = OnCommonCanvas(NativeChannel(mine, c), FreeTypeChannel(reference, c));
                double ssim = ImageSimilarity.Ssim(a, b, w, h);
                totals[c] += ssim;
                report.Append(CultureInfo.InvariantCulture, $"'{character}'[{"RGB"[c]}]={ssim:F3} ");
            }
        }

        for (int c = 0; c < 3; c++)
        {
            double mean = totals[c] / GlyphSample.Length;
            mean.ShouldBeGreaterThan(0.98, $"channel {"RGB"[c]} at {size}px: mean SSIM {mean:F3}\n{report}");
        }
    }

    [Fact]
    public void FreeTypeComparison_IsNotTriviallyOne_ForMismatchedGlyphs()
    {
        // Guards the comparison above: a different glyph must score well under the threshold.
        var options = new RasterOptions { Size = 24, AntiAlias = AntiAliasMode.Lcd, EnableHinting = false };
        var bytes = TestFonts.RobotoRegularBytes();
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);

        var (a, b, w, h) = OnCommonCanvas(
            NativeChannel(native.RasterizeGlyph('O', options)!, 1),
            FreeTypeChannel(freeType.RasterizeGlyph('X', options)!, 1));
        ImageSimilarity.Ssim(a, b, w, h).ShouldBeLessThan(0.85);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static readonly RasterOptions Options = new() { Size = 16, AntiAlias = AntiAliasMode.Lcd };

    private static NativeRasterizer LoadedRoboto()
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        return rasterizer;
    }

    /// <summary>A rectangle whose pixel-space X span is [left, right] and whose rows run from
    /// <paramref name="top"/> down to <paramref name="bottom"/> above the baseline.</summary>
    private static PreparedGlyph Rectangle(float left, float right, float bottom, float top)
    {
        var outline = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(left, bottom),
            OutlineCommand.Line(right, bottom),
            OutlineCommand.Line(right, top),
            OutlineCommand.Line(left, top),
            OutlineCommand.CloseAt(left, bottom),
        ]);
        var box = GlyphBox.Compute(outline, null, 1f);
        return new PreparedGlyph(outline, new OutlineTransform(1f, 0f), box, (int)MathF.Ceiling(right));
    }

    /// <summary>The exact filtered value, 0-255, of global subpixel <paramref name="s"/> for a
    /// full-height bar spanning [left, right].</summary>
    private static double FilteredCoverage(LcdFilter filter, float left, float right, int s)
    {
        double sum = 0;
        for (int k = 0; k < filter.Weights.Count; k++)
        {
            int sub = s - filter.Radius + k;
            double lo = Math.Max(left, sub / 3.0);
            double hi = Math.Min(right, (sub + 1) / 3.0);
            double coverage = Math.Max(0, hi - lo) * 3.0;
            sum += filter.Weights[k] * coverage;
        }

        return 255.0 * sum / filter.Denominator;
    }

    private static PreparedGlyph PrepareGlyph(NativeRasterizer rasterizer, int codepoint, float size)
    {
        int glyphIndex = rasterizer.GetGlyphIndex(codepoint);
        var face = rasterizer.Face!;
        var pipeline = new GlyphPipeline(Internal.Outlines.OutlineDecoders.ForFace(face, null)!, face.Head.UnitsPerEm);
        return pipeline.Prepare(glyphIndex, new RasterOptions { Size = size }, []);
    }

    private static IEnumerable<(byte R, byte G, byte B, byte A)> Pixels(RasterizedGlyph glyph)
    {
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                int i = (y * glyph.Pitch) + (x * 4);
                yield return (glyph.BitmapData[i], glyph.BitmapData[i + 1], glyph.BitmapData[i + 2], glyph.BitmapData[i + 3]);
            }
        }
    }

    /// <summary>One channel of a Native RGBA glyph, with the glyph's pixel origin.</summary>
    private static Plane NativeChannel(RasterizedGlyph glyph, int channel)
    {
        var data = new byte[glyph.Width * glyph.Height];
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
                data[(y * glyph.Width) + x] = glyph.BitmapData[(y * glyph.Pitch) + (x * 4) + channel];
        }

        return new Plane(data, glyph.Width, glyph.Height, glyph.Metrics.BearingX, glyph.Metrics.BearingY);
    }

    /// <summary>
    /// One channel of FreeType's LCD glyph. The FreeType backend hands back FreeType's raw LCD
    /// buffer — three bytes (R, G, B subpixels, left to right) per pixel, so its reported width
    /// is three times the pixel width.
    /// </summary>
    private static Plane FreeTypeChannel(RasterizedGlyph glyph, int channel)
    {
        int width = glyph.Width / 3;
        var data = new byte[width * glyph.Height];
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < width; x++)
                data[(y * width) + x] = glyph.BitmapData[(y * glyph.Pitch) + (x * 3) + channel];
        }

        return new Plane(data, width, glyph.Height, glyph.Metrics.BearingX, glyph.Metrics.BearingY);
    }

    /// <summary>Places two planes on the union of their boxes, aligned by their bearings.</summary>
    private static (byte[] A, byte[] B, int Width, int Height) OnCommonCanvas(Plane a, Plane b)
    {
        int left = Math.Min(a.Left, b.Left);
        int top = Math.Max(a.Top, b.Top);
        int right = Math.Max(a.Left + a.Width, b.Left + b.Width);
        int bottom = Math.Min(a.Top - a.Height, b.Top - b.Height);
        int width = right - left, height = top - bottom;

        return (Place(a, left, top, width, height), Place(b, left, top, width, height), width, height);

        static byte[] Place(Plane plane, int left, int top, int width, int height)
        {
            var canvas = new byte[width * height];
            int dx = plane.Left - left, dy = top - plane.Top;
            for (int y = 0; y < plane.Height; y++)
                Array.Copy(plane.Data, y * plane.Width, canvas, ((y + dy) * width) + dx, plane.Width);

            return canvas;
        }
    }

    /// <summary>A single-channel image and where its top-left pixel sits (X from the pen, Y up
    /// from the baseline).</summary>
    private sealed record Plane(byte[] Data, int Width, int Height, int Left, int Top);
}
