using System.Globalization;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 169 — single-channel signed distance fields computed from the outline: exact
/// distances on shapes with known geometry, sign by non-zero winding (overlaps, counters,
/// either contour direction), the FreeType byte encoding and padding, and agreement with
/// FreeType's own SDF renderer on real glyphs.
/// </summary>
public class SdfRendererTests
{
    private static RasterOptions Sdf(float spread, float size = 32f) =>
        new() { Size = size, Sdf = true, SdfSpread = spread };

    // ---- Dispatch and capability -----------------------------------------------------------

    [Fact]
    public void Dispatch_SelectsTheSdfRenderer_WhenSdfIsRequested()
    {
        GlyphRenderers.Select(Sdf(8)).ShouldBeOfType<SdfRenderer>();
    }

    [Fact]
    public void Capabilities_ReportSdf()
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.Capabilities.SupportsSdf.ShouldBeTrue();
    }

    // ---- Exact distances on known shapes ---------------------------------------------------

    [Theory]
    [InlineData(4f)]
    [InlineData(8f)]
    [InlineData(16f)]
    public void Rectangle_EveryTexelEncodesItsExactSignedDistance(float spread)
    {
        // Fractional corners, so no edge sits on a texel boundary or centre.
        var outline = TestOutlines.Outline(TestOutlines.Rect(3.3f, 2.6f, 23.7f, 21.1f));
        var glyph = Render(outline, spread);

        int pad = (int)spread;
        glyph.Width.ShouldBe(TestOutlines.Prepare(outline).Box.Width + (2 * pad));
        glyph.Height.ShouldBe(TestOutlines.Prepare(outline).Box.Height + (2 * pad));

        AssertField(glyph, spread, (px, py) => TestOutlines.BoxDistance(px, py, 3.3, -21.1, 23.7, -2.6));
    }

    [Fact]
    public void Circle_EveryTexelEncodesItsExactSignedDistance()
    {
        // Curved edges: the distance is to the cubic arcs themselves (radial error of the
        // four-cubic circle is 0.027% of r = 0.005px here), not to a coarse flattening.
        const float cx = 20.4f, cy = 19.7f, r = 18.25f;
        var outline = TestOutlines.Outline(TestOutlines.Circle(cx, cy, r));

        AssertField(Render(outline, 8f), 8f, (px, py) => r - Math.Sqrt(((px - cx) * (px - cx)) + ((py + cy) * (py + cy))));
    }

    [Fact]
    public void Counter_IsOutside_AndTheRingMeasuresToBothContours()
    {
        // A square ring: outer clockwise, inner counter-clockwise (TrueType's counter rule).
        var outline = TestOutlines.Outline(
            TestOutlines.Rect(0.5f, 0.5f, 40.5f, 40.5f),
            TestOutlines.Rect(12.5f, 12.5f, 28.5f, 28.5f, reversed: true));

        AssertField(Render(outline, 8f), 8f, (px, py) => Math.Min(
            TestOutlines.BoxDistance(px, py, 0.5, -40.5, 40.5, -0.5),
            -TestOutlines.BoxDistance(px, py, 12.5, -28.5, 28.5, -12.5)));
    }

    [Fact]
    public void ReversedContours_ProduceTheSameField()
    {
        // Non-zero winding, not contour direction, decides inside — CFF fonts wind the
        // opposite way to TrueType.
        var clockwise = TestOutlines.Outline(
            TestOutlines.Rect(0.5f, 0.5f, 40.5f, 40.5f),
            TestOutlines.Rect(12.5f, 12.5f, 28.5f, 28.5f, reversed: true));
        var counterClockwise = TestOutlines.Outline(
            TestOutlines.Rect(0.5f, 0.5f, 40.5f, 40.5f, reversed: true),
            TestOutlines.Rect(12.5f, 12.5f, 28.5f, 28.5f));

        Render(counterClockwise, 8f).BitmapData.ShouldBe(Render(clockwise, 8f).BitmapData);
    }

    // ---- Overlapping contours --------------------------------------------------------------

    [Fact]
    public void OverlappingContours_MeasureToTheUnionOutline_WithNoInteriorSeam()
    {
        // A plus sign drawn as two overlapping bars must produce the same field as the same
        // plus drawn as one 12-vertex contour. Measuring to the bars' hidden interior edges
        // would put a ~128 seam through the middle of the glyph.
        var bars = TestOutlines.Outline(
            TestOutlines.Rect(0.4f, 10.4f, 30.4f, 20.4f),
            TestOutlines.Rect(10.4f, 0.4f, 20.4f, 30.4f));
        var plus = TestOutlines.Outline(TestOutlines.Polygon(
            (0.4f, 10.4f), (0.4f, 20.4f), (10.4f, 20.4f), (10.4f, 30.4f), (20.4f, 30.4f), (20.4f, 20.4f),
            (30.4f, 20.4f), (30.4f, 10.4f), (20.4f, 10.4f), (20.4f, 0.4f), (10.4f, 0.4f), (10.4f, 10.4f)));

        AssertSameWithinOne(Render(bars, 8f), Render(plus, 8f));
    }

    [Fact]
    public void CollinearOverlap_AndNestedSameDirectionContours_MeasureToTheUnion()
    {
        // Two squares sharing their top and bottom edges over the overlap, plus a third square
        // entirely inside the first with the same winding: the union is one 30x20 rectangle.
        var pieces = TestOutlines.Outline(
            TestOutlines.Rect(0.5f, 0.5f, 20.5f, 20.5f),
            TestOutlines.Rect(10.5f, 0.5f, 30.5f, 20.5f),
            TestOutlines.Rect(4.5f, 4.5f, 8.5f, 8.5f));

        AssertField(Render(pieces, 6f), 6f, (px, py) => TestOutlines.BoxDistance(px, py, 0.5, -20.5, 30.5, -0.5));
    }

    // ---- Metrics ---------------------------------------------------------------------------

    [Theory]
    [InlineData(2f, 2)]
    [InlineData(6f, 6)]
    [InlineData(6.5f, 7)]
    [InlineData(40f, 40)]
    public void Padding_IsTheRoundedSpread_OnEverySide(float spread, int expectedPad)
    {
        var outline = TestOutlines.Outline(TestOutlines.Rect(2f, 3f, 12f, 23f));
        var prepared = TestOutlines.Prepare(outline, advance: 15);

        var metrics = SdfRenderer.Instance.Measure(prepared, Sdf(spread));

        metrics.ShouldBe(new GlyphMetrics(
            BearingX: prepared.Box.BearingX - expectedPad,
            BearingY: prepared.Box.BearingY + expectedPad,
            Advance: 15,
            Width: prepared.Box.Width + (2 * expectedPad),
            Height: prepared.Box.Height + (2 * expectedPad)));
    }

    [Theory]
    [InlineData(12f, 4f)]
    [InlineData(32f, 8f)]
    [InlineData(48f, 6f)]
    public void GetGlyphMetrics_EqualsTheRenderedGlyph_ForEveryAsciiGlyph(float size, float spread)
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        var options = Sdf(spread, size);

        for (int codepoint = 0x20; codepoint < 0x7F; codepoint++)
        {
            var glyph = rasterizer.RasterizeGlyph(codepoint, options);
            glyph.ShouldNotBeNull();
            rasterizer.GetGlyphMetrics(codepoint, options).ShouldBe(glyph.Metrics, $"U+{codepoint:X4}");
            glyph.Format.ShouldBe(PixelFormat.Grayscale8);
            glyph.Pitch.ShouldBe(glyph.Width);
            glyph.BitmapData.Length.ShouldBe(glyph.Width * glyph.Height);
        }
    }

    // ---- Field quality on real glyphs ------------------------------------------------------

    [Theory]
    [InlineData(4f)]
    [InlineData(8f)]
    [InlineData(16f)]
    public void Field_HasNoDiscontinuities(float spread)
    {
        // A true distance field is 1-Lipschitz: neighbouring texels differ by at most one
        // pixel of distance, i.e. 128/spread code values (plus one for the floor). A seam,
        // a sign flip or a missed edge breaks this.
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        int limit = (int)Math.Ceiling(128 / spread) + 1;

        foreach (char c in "AaBgO8&%@Wkxé")
        {
            var glyph = rasterizer.RasterizeGlyph(c, Sdf(spread, 48f))!;
            for (int y = 0; y < glyph.Height; y++)
            {
                for (int x = 0; x < glyph.Width; x++)
                {
                    int v = glyph.BitmapData[(y * glyph.Width) + x];
                    if (x + 1 < glyph.Width)
                        Math.Abs(v - glyph.BitmapData[(y * glyph.Width) + x + 1]).ShouldBeLessThanOrEqualTo(limit, $"'{c}' ({x},{y}) right");
                    if (y + 1 < glyph.Height)
                        Math.Abs(v - glyph.BitmapData[((y + 1) * glyph.Width) + x]).ShouldBeLessThanOrEqualTo(limit, $"'{c}' ({x},{y}) down");
                }
            }
        }
    }

    [Fact]
    public void LetterA_EdgeIs128_InsideBright_OutsideDark_AndTheRowIsMonotoneAcrossEachEdge()
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        var glyph = rasterizer.RasterizeGlyph('A', Sdf(8f, 64f))!;

        // The padding ring is at least `spread` from the ink, so it is fully outside.
        glyph.BitmapData[0].ShouldBe((byte)0);
        glyph.BitmapData[^1].ShouldBe((byte)0);

        // A row between the apex and the crossbar, where the legs are separate: walk it and
        // check every 128 crossing is approached monotonically from both sides.
        int row = glyph.Height * 2 / 5;
        var values = Enumerable.Range(0, glyph.Width).Select(x => (int)glyph.BitmapData[(row * glyph.Width) + x]).ToArray();
        values.ShouldContain(v => v > 128);
        int crossings = 0;
        for (int x = 3; x < values.Length - 3; x++)
        {
            bool rising = values[x - 1] < 128 && values[x] >= 128;
            bool falling = values[x - 1] >= 128 && values[x] < 128;
            if (!rising && !falling)
                continue;

            crossings++;
            var window = values[(x - 3)..(x + 3)];
            for (int i = 1; i < window.Length; i++)
            {
                if (rising)
                    window[i].ShouldBeGreaterThan(window[i - 1], $"rising edge at x={x}: {string.Join(",", window)}");
                else
                    window[i].ShouldBeLessThan(window[i - 1], $"falling edge at x={x}: {string.Join(",", window)}");
            }
        }

        crossings.ShouldBe(4, "the row crosses both legs of the A");
    }

    [Theory]
    [InlineData('A', 48f)]
    [InlineData('g', 48f)]
    [InlineData('@', 32f)]
    [InlineData('O', 96f)]
    public void ThresholdAt128_ReproducesTheCoverageBitmap(char c, float size)
    {
        // Wherever coverage is unambiguous (well clear of 50%), the field's sign must agree.
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        var coverage = rasterizer.RasterizeGlyph(c, new RasterOptions { Size = size })!;
        var field = rasterizer.RasterizeGlyph(c, Sdf(8f, size))!;

        int dx = coverage.Metrics.BearingX - field.Metrics.BearingX;
        int dy = field.Metrics.BearingY - coverage.Metrics.BearingY;
        dx.ShouldBe(8);
        dy.ShouldBe(8);

        int disagreements = 0;
        for (int y = 0; y < coverage.Height; y++)
        {
            for (int x = 0; x < coverage.Width; x++)
            {
                int cov = coverage.BitmapData[(y * coverage.Width) + x];
                if (cov is > 64 and < 192)
                    continue;

                bool sdfInside = field.BitmapData[((y + dy) * field.Width) + x + dx] >= 128;
                if (sdfInside != (cov >= 192))
                    disagreements++;
            }
        }

        disagreements.ShouldBe(0);
    }

    public static TheoryData<char, float, float> FreeTypeCases => new()
    {
        { 'A', 32f, 4f }, { 'A', 64f, 8f },
        { 'g', 32f, 8f }, { 'g', 64f, 4f },
        { 'O', 48f, 6f }, { '&', 48f, 8f },
        { 'S', 64f, 16f }, { '@', 32f, 4f },
    };

    [Theory]
    [MemberData(nameof(FreeTypeCases))]
    public void MatchesFreeTypeSdf(char c, float size, float spread)
    {
        // FreeType is the behavioural baseline: same padding, bearings and byte encoding.
        // Hinting off on both sides — Native has no hinter.
        var options = new RasterOptions { Size = size, Sdf = true, SdfSpread = spread, EnableHinting = false };
        var bytes = TestFonts.RobotoRegularBytes();

        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);

        var expected = freeType.RasterizeGlyph(c, options)!;
        var actual = native.RasterizeGlyph(c, options)!;

        // Same outline, same box rule: the cells must coincide exactly.
        actual.Metrics.ShouldBe(expected.Metrics with { Advance = actual.Metrics.Advance });
        actual.Width.ShouldBe(expected.Width);
        actual.Height.ShouldBe(expected.Height);

        double ssim = ImageSimilarity.Ssim(actual.BitmapData, expected.BitmapData, actual.Width, actual.Height);
        var diffs = actual.BitmapData.Zip(expected.BitmapData, (x, y) => Math.Abs(x - y)).ToArray();

        // FreeType measures curves iteratively in 16.16 fixed point; Native measures exactly
        // to a polyline within spread/512 px of the curve. They agree to within two code values.
        ssim.ShouldBeGreaterThan(0.99, $"'{c}' {size}px spread {spread}: SSIM {ssim:F4}");
        diffs.Average().ShouldBeLessThan(0.5, $"'{c}' {size}px spread {spread}: mean |diff|");
        diffs.Max().ShouldBeLessThanOrEqualTo(2, $"'{c}' {size}px spread {spread}: max |diff|");
    }

    // ---- End to end ------------------------------------------------------------------------

    [Theory]
    [InlineData(2)]
    [InlineData(1)]
    public void Generate_WithTheSdfHarnessSettings_ProducesSdfGlyphsOnNative(int sdfScale)
    {
        // The settings of tests/bmfont-compare/gum-bmfont/sdf.bmfc (spread 6, scale 2) and its
        // scale-1 sibling, on the committed Roboto fixture. The harness tolerates a backend
        // failing a config, so this is what proves Native really renders it.
        var options = new FontGeneratorOptions
        {
            Size = 48,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.Native,
            Sdf = true,
            SdfSpread = 6,
            SdfScale = sdfScale,
        };

        var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), options);
        result.FntText.ShouldContain("sdfSpread=6", Case.Sensitive);

        var model = BmFontReader.ReadText(result.FntText);
        var a = model.Characters.First(ch => ch.Id == 'A');
        var page = result.Pages[a.Page];

        // An SDF cell has mid-range values throughout its padding ramp, not just 0/255
        // ink with a one-pixel anti-aliased rim.
        int midRange = 0;
        for (int y = a.Y; y < a.Y + a.Height; y++)
        {
            for (int x = a.X; x < a.X + a.Width; x++)
            {
                byte v = page.PixelData[(y * page.Width) + x];
                if (v is > 20 and < 235)
                    midRange++;
            }
        }

        midRange.ShouldBeGreaterThan(a.Width * a.Height / 4, "cell does not look like a distance field");
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static RasterizedGlyph Render(GlyphOutline outline, float spread) =>
        SdfRenderer.Instance.Render('x', 1, TestOutlines.Prepare(outline), Sdf(spread));

    /// <summary>
    /// Every texel must equal FreeType's encoding of the exact signed distance from its centre,
    /// within one code value (the floor in the encoding can tip either way on rounding noise).
    /// </summary>
    private static void AssertField(RasterizedGlyph glyph, float spread, Func<double, double, double> signedDistance)
    {
        var failures = new StringBuilder();
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                double px = glyph.Metrics.BearingX + x + 0.5;
                double py = -glyph.Metrics.BearingY + y + 0.5;
                int expected = TestOutlines.EncodeLikeFreeType(signedDistance(px, py), spread);
                int actual = glyph.BitmapData[(y * glyph.Width) + x];
                if (Math.Abs(actual - expected) > 1 && failures.Length < 2000)
                    failures.Append(CultureInfo.InvariantCulture, $"({x},{y}) expected {expected} got {actual}\n");
            }
        }

        failures.ToString().ShouldBeEmpty();
    }

    private static void AssertSameWithinOne(RasterizedGlyph actual, RasterizedGlyph expected)
    {
        actual.Metrics.ShouldBe(expected.Metrics);
        for (int i = 0; i < expected.BitmapData.Length; i++)
            Math.Abs(actual.BitmapData[i] - expected.BitmapData[i]).ShouldBeLessThanOrEqualTo(1, $"texel {i % expected.Width},{i / expected.Width}");
    }
}
