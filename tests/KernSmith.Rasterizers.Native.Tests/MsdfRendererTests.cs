using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Distance;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 170 — multi-channel distance fields: edge coloring, per-channel pseudo-distances,
/// median reconstruction (sharp corners), clash correction, MTSDF alpha, and RGBA output.
/// </summary>
public class MsdfRendererTests
{
    private static RasterOptions Options(SdfMode mode, float spread = 4f, float size = 32f) =>
        new() { Size = size, SdfMode = mode, SdfSpread = spread };

    // ---- Dispatch and capability -----------------------------------------------------------

    [Fact]
    public void Dispatch_SelectsTheRendererForTheEffectiveMode()
    {
        GlyphRenderers.Select(Options(SdfMode.Msdf)).ShouldBeSameAs(MsdfRenderer.Msdf);
        GlyphRenderers.Select(Options(SdfMode.Mtsdf)).ShouldBeSameAs(MsdfRenderer.Mtsdf);
        GlyphRenderers.Select(Options(SdfMode.Sdf)).ShouldBeSameAs(SdfRenderer.Instance);
        GlyphRenderers.Select(new RasterOptions { Size = 32, Sdf = true, SdfMode = SdfMode.Msdf }).ShouldBeSameAs(MsdfRenderer.Msdf);
    }

    [Fact]
    public void Capabilities_ReportMsdf()
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.Capabilities.SupportsMsdf.ShouldBeTrue();
    }

    // ---- Edge coloring ---------------------------------------------------------------------

    [Fact]
    public void Square_EveryCornerJoinsTwoDifferentTwoChannelColors()
    {
        var shape = Colored(TestOutlines.Outline(TestOutlines.Rect(0.5f, 0.5f, 20.5f, 20.5f)));

        shape.Contours.Count.ShouldBe(1);
        AssertCornersDiffer(shape);
        foreach (var edge in shape.Edges)
            edge.Color.ShouldBeOneOf(EdgeColor.Cyan, EdgeColor.Magenta, EdgeColor.Yellow);
    }

    [Fact]
    public void SmoothContour_StaysWhite()
    {
        var shape = Colored(TestOutlines.Outline(TestOutlines.Circle(20, 20, 15)));

        foreach (var edge in shape.Edges)
            edge.Color.ShouldBe(EdgeColor.White);
    }

    [Fact]
    public void Teardrop_IsSplitIntoThreeColorsWithDifferentColorsAtItsCorner()
    {
        // A single cubic looping back to its start: one edge, one corner where its end
        // tangent meets its start tangent. It must still get two colors at the corner.
        var outline = TestOutlines.Outline(
        [
            OutlineCommand.Move(0, 0),
            OutlineCommand.Cubic(40, 40, -40, 40, 0, 0),
            OutlineCommand.CloseAt(0, 0),
        ]);
        var shape = Colored(outline);

        shape.Contours[0].Count.ShouldBeGreaterThanOrEqualTo(3);
        AssertCornersDiffer(shape);
        shape.Contours[0].Select(e => shape.Edges[e].Color).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public void RobotoGlyphs_EveryCornerJoinsDifferentColors()
    {
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        for (int c = 0x21; c < 0x7F; c++)
        {
            var outline = OutlineExtractor.Extract(face.GetGlyph(face.GetGlyphIndex(c)));
            if (outline.IsEmpty)
                continue;

            AssertCornersDiffer(Colored(outline, scale: 0.05f), $"'{(char)c}'");
        }
    }

    // ---- Reconstruction --------------------------------------------------------------------

    [Fact]
    public void Median_RecoversSharpCorners_ThatSingleChannelSdfRoundsOff()
    {
        // A square whose corners fall between texel centres. Reconstruct the shape at 8x by
        // bilinear interpolation (what a GPU sampler does) and count misclassified samples.
        var outline = TestOutlines.Outline(TestOutlines.Rect(4.3f, 3.6f, 15.7f, 14.2f));
        const int pad = 4;
        var (shape, width, height, originX, originY) = Field(outline, pad);

        var sdf = SignedDistanceField.Compute(shape, width, height, pad);
        var msdf = MultiChannelDistanceField.Compute(shape, width, height, pad);
        MultiChannelDistanceField.CorrectClashes(msdf, width, height);

        int sdfErrors = 0, msdfErrors = 0, samples = 0;
        const int k = 8;
        for (int sy = 0; sy < (height - 1) * k; sy++)
        {
            for (int sx = 0; sx < (width - 1) * k; sx++)
            {
                double fx = (double)sx / k, fy = (double)sy / k; // in texel-centre space
                double px = originX + fx + 0.5, py = originY + fy + 0.5;
                bool truth = px > 4.3 && px < 15.7 && py > -14.2 && py < -3.6;

                if (Sample(sdf, width, 1, 0, fx, fy) >= 0 != truth)
                    sdfErrors++;

                double r = Sample(msdf, width, 3, 0, fx, fy), g = Sample(msdf, width, 3, 1, fx, fy), b = Sample(msdf, width, 3, 2, fx, fy);
                if (Math.Max(Math.Min(r, g), Math.Min(Math.Max(r, g), b)) >= 0 != truth)
                    msdfErrors++;
                samples++;
            }
        }

        // Single-channel SDF misclassifies the samples its rounded corners cut off (48 of
        // 23104 here). Near a corner each MSDF channel is the distance to one edge's line —
        // linear, so bilinear sampling reproduces it — and the median is exactly the square.
        sdfErrors.ShouldBeGreaterThan(20, "the corners should visibly round on single-channel SDF");
        msdfErrors.ShouldBe(0, $"MSDF {msdfErrors} vs SDF {sdfErrors} of {samples}");
    }

    [Fact]
    public void Circle_MsdfChannelsAllEqualTheSingleChannelField()
    {
        // No corners: every edge is white, so all three channels are the plain SDF.
        var prepared = TestOutlines.Prepare(TestOutlines.Outline(TestOutlines.Circle(20.4f, 19.7f, 14.25f)));
        var sdf = SdfRenderer.Instance.Render('o', 1, prepared, Options(SdfMode.Sdf));
        var msdf = MsdfRenderer.Msdf.Render('o', 1, prepared, Options(SdfMode.Msdf));

        for (int i = 0; i < sdf.BitmapData.Length; i++)
        {
            msdf.BitmapData[(4 * i) + 0].ShouldBe(sdf.BitmapData[i]);
            msdf.BitmapData[(4 * i) + 1].ShouldBe(sdf.BitmapData[i]);
            msdf.BitmapData[(4 * i) + 2].ShouldBe(sdf.BitmapData[i]);
            msdf.BitmapData[(4 * i) + 3].ShouldBe((byte)255);
        }
    }

    [Fact]
    public void OverlappingContours_MedianSignMatchesTheSingleChannelField()
    {
        var bars = TestOutlines.Outline(
            TestOutlines.Rect(0.4f, 10.4f, 30.4f, 20.4f),
            TestOutlines.Rect(10.4f, 0.4f, 20.4f, 30.4f));
        var prepared = TestOutlines.Prepare(bars);
        var sdf = SdfRenderer.Instance.Render('+', 1, prepared, Options(SdfMode.Sdf));
        var msdf = MsdfRenderer.Msdf.Render('+', 1, prepared, Options(SdfMode.Msdf));

        for (int i = 0; i < sdf.BitmapData.Length; i++)
        {
            int median = Median(msdf.BitmapData, i);
            if (Math.Abs(sdf.BitmapData[i] - 128) > 2)
                (median >= 128).ShouldBe(sdf.BitmapData[i] >= 128, $"texel {i % sdf.Width},{i / sdf.Width}");
        }
    }

    [Theory]
    [InlineData(16f, 4f)]
    [InlineData(32f, 4f)]
    [InlineData(48f, 6f)]
    public void ClashCorrection_LeavesNoInterpolationArtifacts_OnRobotoAscii(float size, float spread)
    {
        // Between two adjacent texels that are both more than half a pixel inside (or outside)
        // the true outline, the edge cannot pass, so the interpolated median there must keep
        // their sign. Where it flips, the channels clash — the classic MSDF artifact.
        var face = NativeFontFace.Load(TestFonts.RobotoRegularBytes());
        var pipeline = new GlyphPipeline(new TrueTypeOutlineDecoder(face), face.Head.UnitsPerEm);
        var options = Options(SdfMode.Msdf, spread, size);
        int artifacts = 0;

        for (int c = 0x21; c < 0x7F; c++)
        {
            var glyph = pipeline.Prepare(face.GetGlyphIndex(c), options, []);
            int pad = SdfRenderer.SpreadPixels(options);
            int width = glyph.Box.Width + (2 * pad), height = glyph.Box.Height + (2 * pad);
            var shape = DistanceShape.Build(glyph.Outline, glyph.ToPixels, glyph.Box.MinX - pad, glyph.Box.MinY - pad,
                SignedDistanceField.ToleranceFor(pad), colorEdges: true);

            var truth = SignedDistanceField.Compute(shape, width, height, pad);
            var msdf = MultiChannelDistanceField.Compute(shape, width, height, pad);
            MultiChannelDistanceField.CorrectClashes(msdf, width, height);

            artifacts += CountArtifacts(truth, msdf, width, height);
        }

        artifacts.ShouldBe(0);
    }

    // ---- Output ----------------------------------------------------------------------------

    [Theory]
    [InlineData(SdfMode.Msdf)]
    [InlineData(SdfMode.Mtsdf)]
    public void Output_IsRgbaWithSdfPadding_AndMeasureEqualsRender(SdfMode mode)
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        var options = Options(mode, 6f, 40f);
        var sdfOptions = Options(SdfMode.Sdf, 6f, 40f);

        for (int c = 0x20; c < 0x7F; c++)
        {
            var glyph = rasterizer.RasterizeGlyph(c, options)!;
            rasterizer.GetGlyphMetrics(c, options).ShouldBe(glyph.Metrics, $"U+{c:X4}");
            glyph.Metrics.ShouldBe(rasterizer.GetGlyphMetrics(c, sdfOptions)!.Value, "same padding and bearings as SDF");
            if (glyph.Width == 0)
                continue;

            glyph.Format.ShouldBe(PixelFormat.Rgba32);
            glyph.Pitch.ShouldBe(glyph.Width * 4);
            glyph.BitmapData.Length.ShouldBe(glyph.Width * glyph.Height * 4);
        }
    }

    [Fact]
    public void Msdf_AlphaIsOpaque_AndMtsdfAlphaIsTheSingleChannelSdf()
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());

        foreach (char c in "AaBgO8&%@Wkxé")
        {
            var sdf = rasterizer.RasterizeGlyph(c, Options(SdfMode.Sdf, 6f, 40f))!;
            var msdf = rasterizer.RasterizeGlyph(c, Options(SdfMode.Msdf, 6f, 40f))!;
            var mtsdf = rasterizer.RasterizeGlyph(c, Options(SdfMode.Mtsdf, 6f, 40f))!;

            for (int i = 0; i < sdf.BitmapData.Length; i++)
            {
                msdf.BitmapData[(4 * i) + 3].ShouldBe((byte)255);
                mtsdf.BitmapData[(4 * i) + 3].ShouldBe(sdf.BitmapData[i], $"'{c}' texel {i}");
                mtsdf.BitmapData[(4 * i) + 0].ShouldBe(msdf.BitmapData[(4 * i) + 0]);
            }
        }
    }

    [Theory]
    [InlineData('A', 48f)]
    [InlineData('g', 48f)]
    [InlineData('@', 32f)]
    public void MedianThresholdAt128_ReproducesTheCoverageBitmap(char c, float size)
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        var coverage = rasterizer.RasterizeGlyph(c, new RasterOptions { Size = size })!;
        var field = rasterizer.RasterizeGlyph(c, Options(SdfMode.Msdf, 8f, size))!;
        int pad = 8;

        int disagreements = 0;
        for (int y = 0; y < coverage.Height; y++)
        {
            for (int x = 0; x < coverage.Width; x++)
            {
                int cov = coverage.BitmapData[(y * coverage.Width) + x];
                if (cov is > 64 and < 192)
                    continue;

                bool inside = Median(field.BitmapData, ((y + pad) * field.Width) + x + pad) >= 128;
                if (inside != (cov >= 192))
                    disagreements++;
            }
        }

        disagreements.ShouldBe(0);
    }

    [Theory]
    [InlineData(SdfMode.Msdf, 4)]
    [InlineData(SdfMode.Mtsdf, 0)]
    public void Generate_WithNative_ProducesAnRgbaMultiChannelFont(SdfMode mode, int alphaChannel)
    {
        // The settings of tests/bmfont-compare/gum-bmfont/msdf.bmfc on the Roboto fixture: the
        // harness tolerates a failing pair, so this proves Native renders the config.
        var options = new FontGeneratorOptions
        {
            Size = 48,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.Native,
            SdfMode = mode,
            SdfSpread = 6,
            SdfScale = 2,
        };

        var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), options);

        result.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);
        result.Model.Common.AlphaChnl.ShouldBe(alphaChannel);
        result.FntText.ShouldContain("sdfSpread=6", Case.Sensitive);
        result.FntText.ShouldContain($"sdfMode={mode.ToString().ToLowerInvariant()}", Case.Sensitive);

        // The cells hold distance ramps in all three colour channels.
        var model = BmFontReader.ReadText(result.FntText);
        var a = model.Characters.First(ch => ch.Id == 'A');
        var page = result.Pages[a.Page];
        var midRange = new int[3];
        for (int y = a.Y; y < a.Y + a.Height; y++)
        {
            for (int x = a.X; x < a.X + a.Width; x++)
            {
                for (int ch = 0; ch < 3; ch++)
                {
                    if (page.PixelData[(((y * page.Width) + x) * 4) + ch] is > 20 and < 235)
                        midRange[ch]++;
                }
            }
        }

        midRange.ShouldAllBe(n => n > a.Width * a.Height / 5);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static DistanceShape Colored(GlyphOutline outline, float scale = 1f) =>
        DistanceShape.Build(outline, new OutlineTransform(scale, 0f), 0, 0, 1f / 64, colorEdges: true);

    private static void AssertCornersDiffer(DistanceShape shape, string what = "")
    {
        foreach (var contour in shape.Contours)
        {
            bool anyCorner = false;
            for (int i = 0; i < contour.Count; i++)
            {
                if (!EdgeColoring.IsCornerBefore(contour, shape.Edges, i))
                    continue;

                anyCorner = true;
                var before = shape.Edges[contour[(i + contour.Count - 1) % contour.Count]].Color;
                var after = shape.Edges[contour[i]].Color;

                // Two colors that each keep a channel the other lacks: neither contains the other.
                ((before & after) != before && (before & after) != after)
                    .ShouldBeTrue($"{what} corner before edge {i}: {before} then {after}");
            }

            if (!anyCorner)
                contour.ShouldAllBe(e => shape.Edges[e].Color == EdgeColor.White, $"{what} smooth contour");
        }
    }

    private static (DistanceShape Shape, int Width, int Height, int OriginX, int OriginY) Field(GlyphOutline outline, int pad)
    {
        var prepared = TestOutlines.Prepare(outline);
        int originX = prepared.Box.MinX - pad, originY = prepared.Box.MinY - pad;
        var shape = DistanceShape.Build(outline, TestOutlines.Identity, originX, originY, 1f / 128, colorEdges: true);
        return (shape, prepared.Box.Width + (2 * pad), prepared.Box.Height + (2 * pad), originX, originY);
    }

    /// <summary>Bilinear sample of one channel at texel-centre coordinates (fx, fy).</summary>
    private static double Sample(float[] field, int width, int channels, int channel, double fx, double fy)
    {
        int x0 = (int)fx, y0 = (int)fy;
        double tx = fx - x0, ty = fy - y0;
        double At(int x, int y) => field[(((y * width) + x) * channels) + channel];
        double top = (At(x0, y0) * (1 - tx)) + (At(x0 + 1, y0) * tx);
        double bottom = (At(x0, y0 + 1) * (1 - tx)) + (At(x0 + 1, y0 + 1) * tx);
        return (top * (1 - ty)) + (bottom * ty);
    }

    private static int Median(byte[] rgba, int texel)
    {
        int r = rgba[4 * texel], g = rgba[(4 * texel) + 1], b = rgba[(4 * texel) + 2];
        return Math.Max(Math.Min(r, g), Math.Min(Math.Max(r, g), b));
    }

    private static int CountArtifacts(float[] truth, float[] msdf, int width, int height)
    {
        int count = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x + 1 < width)
                    count += Check(y * width + x, y * width + x + 1);
                if (y + 1 < height)
                    count += Check(y * width + x, (y + 1) * width + x);
            }
        }

        return count;

        int Check(int a, int b)
        {
            if (Math.Abs(truth[a]) <= 0.5f || Math.Abs(truth[b]) <= 0.5f || (truth[a] > 0) != (truth[b] > 0))
                return 0;

            float r = (msdf[3 * a] + msdf[3 * b]) / 2, g = (msdf[(3 * a) + 1] + msdf[(3 * b) + 1]) / 2, bl = (msdf[(3 * a) + 2] + msdf[(3 * b) + 2]) / 2;
            float median = MultiChannelDistanceField.Median(r, g, bl);
            return (median > 0) != (truth[a] > 0) ? 1 : 0;
        }
    }
}
