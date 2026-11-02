using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Hinting;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// How much horizontal ink the <c>Normal</c> x fit leaves a glyph, measured against the
/// unhinted outline on real fonts: a serif glyph keeps the reach of its serifs (they follow
/// their stem rather than snapping to the grid on their own), and a bowl is never fitted
/// narrower than its design width rounds to.
/// </summary>
public class NativeInkWidthTests
{
    private static readonly string Fixtures = Path.GetDirectoryName(TestFonts.RobotoRegularPath)!;

    /// <summary>Source Serif 4's em size for a BMFont cell height (upem 1000, winAscent 1036 + winDescent 335).</summary>
    private static float SerifCell(float cell) => cell * 1000f / 1371f;

    [Fact]
    public void SourceSerif_SerifTips_FollowTheirStems()
    {
        // At a 12 px cell (8.75 ppem) Source Serif's foot and flag serifs are 0.3 px hairlines
        // whose tips end 0.6-0.7 px beyond the stem. The tips on one side of a glyph sit at
        // one x, so the fitter merges them into one edge; as a 3.5 px "straight edge" that
        // edge was snapped to the grid by itself, which moved the serifs up to half a pixel
        // away from their stem and changed the glyph's width: 'x' lost 0.34 px and a column,
        // 'k' grew 0.56 px, 'r' and 'l' 0.4 px. Two 0.3 px tips are 0.6 px of edge — too short
        // to hold a place — so they follow their stem and the width is the font's. (The 'f' is
        // not in this set: its crossbar is a hairline lifted to 0.6 px, so its two bar ends are
        // 1.2 px of edge and snap together as the ends of an '=' do.)
        var face = NativeFontFace.Load(TestFonts.SourceSerifOtfBytes());
        var pipeline = PipelineFor(face);
        float size = SerifCell(12f);

        var failures = new StringBuilder();
        foreach (char c in "xkrl")
        {
            float growth = FittedExtent(pipeline, face, c, size) - UnhintedExtent(pipeline, face, c, size);
            if (MathF.Abs(growth) > 1f / 16f)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' grew {growth:F2} px; ");
        }

        // 'n' and 'h' still grow by their stem pitch rounding (2.66 px to 3), but no longer by
        // the left serifs snapping away from the stem on top of it (0.79 px in all).
        foreach (char c in "nh")
        {
            float growth = FittedExtent(pipeline, face, c, size) - UnhintedExtent(pipeline, face, c, size);
            if (growth > 0.5f)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' grew {growth:F2} px; ");
        }

        failures.ToString().ShouldBeEmpty();

        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.SourceSerifOtfBytes());
        var hinted = native.RasterizeGlyph('x', new RasterOptions { Size = size, HintLevel = HintLevel.Normal })!;
        var unhinted = native.RasterizeGlyph('x', new RasterOptions { Size = size, HintLevel = HintLevel.None })!;
        hinted.Width.ShouldBe(unhinted.Width, "the 'x' has no stem to fit: its box is the font's");
    }

    public static TheoryData<string, float> RoundGlyphCases
    {
        get
        {
            var data = new TheoryData<string, float>();
            foreach (float cell in new[] { 10f, 12f, 14f })
            {
                data.Add("Roboto-Regular.ttf", cell * 2048f / 2701f);
                data.Add("NotoSans-Regular-Latin.ttf", cell * 1000f / 1362f);
                data.Add("SourceSerif4-Regular.otf", SerifCell(cell));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(RoundGlyphCases))]
    public void Grayscale_RoundGlyphs_KeepTheirDesignWidth_ToTheNearestColumn(string font, float size)
    {
        // A bowl's two walls are each placed inside one column at their true coverage, the
        // second at the whole-pixel pitch nearest its distance from the first (a stem, where
        // the glyph has one, anchors instead). The glyph's ink therefore spans round(pitch) + 1
        // columns, which is never fewer than its design width rounds to and at most one more
        // (the wall's own width can push the pitch over a half): Source Serif's 'o' at 8.75
        // ppem is 4.02 px wide and lands in 4 columns, Roboto's 4.38 px 'o' at 9.1 ppem in 5.
        // A fit that lost a column here would leave the glyph a blank column inside its
        // advance and the word a gap. Ink columns are those with any pixel at 32 or more, so
        // a curve's sliver past a column boundary does not count as a column.
        var bytes = File.ReadAllBytes(Path.Combine(Fixtures, font));
        var face = NativeFontFace.Load(bytes);
        var pipeline = PipelineFor(face);
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);

        var failures = new StringBuilder();
        foreach (char c in "ocesabdpq")
        {
            float design = UnhintedExtent(pipeline, face, c, size);
            int expected = (int)MathF.Floor(design + 0.5f);
            int columns = InkColumns(native.RasterizeGlyph(c, new RasterOptions { Size = size, HintLevel = HintLevel.Normal })!);
            if (columns < expected || columns > expected + 1)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' {columns} ink columns for a {design:F2} px design ({FittedExtent(pipeline, face, c, size):F2} px fitted); ");
        }

        failures.ToString().ShouldBeEmpty($"{font} at {size:F2} px");
    }

    /// <summary>The number of columns, first to last, with a pixel at 32 or more.</summary>
    private static int InkColumns(RasterizedGlyph g)
    {
        int first = -1, last = -1;
        for (int x = 0; x < g.Width; x++)
        {
            for (int y = 0; y < g.Height; y++)
            {
                if (g.BitmapData[(y * g.Pitch) + x] < 32)
                    continue;
                if (first < 0)
                    first = x;
                last = x;
                break;
            }
        }

        return first < 0 ? 0 : last - first + 1;
    }

    private static GlyphPipeline PipelineFor(NativeFontFace face) =>
        new(OutlineDecoders.ForFace(face)!, face.Head.UnitsPerEm,
            new GlyphHinter(face.GetGlyphIndex, face.Os2, () => 0, fixedPitch: false, HintingProfile.For(face)));

    /// <summary>The unhinted outline's tight x extent in pixels.</summary>
    private static float UnhintedExtent(GlyphPipeline pipeline, NativeFontFace face, char c, float size)
    {
        var glyph = pipeline.Prepare(face.GetGlyphIndex(c), new RasterOptions { Size = size, HintLevel = HintLevel.None }, []);
        var bounds = OutlineBounds.Tight(GridMath.ScaleOutline(glyph.Outline, glyph.ToPixels.Scale));
        return bounds.XMax - bounds.XMin;
    }

    /// <summary>The <c>Normal</c>-fitted outline's tight x extent in pixels.</summary>
    private static float FittedExtent(GlyphPipeline pipeline, NativeFontFace face, char c, float size)
    {
        var glyph = pipeline.Prepare(face.GetGlyphIndex(c), new RasterOptions { Size = size, HintLevel = HintLevel.Normal }, []);
        var bounds = OutlineBounds.Tight(GridMath.ScaleOutline(glyph.Outline, glyph.ToPixels.Scale));
        return bounds.XMax - bounds.XMin;
    }
}
