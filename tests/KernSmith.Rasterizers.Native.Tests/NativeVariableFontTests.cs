using System.Globalization;
using System.Text;
using KernSmith.Font.Tables;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 171 — TrueType variable fonts end to end: Native at a chosen instance agrees with
/// FreeType (outlines, advances, bearings), font metrics follow <c>MVAR</c>, advances fall back to
/// phantom points without <c>HVAR</c>, and the default instance is untouched.
/// </summary>
/// <remarks>
/// FreeType runs unhinted, as in <see cref="NativeMetricsAgreementTests"/>: Native has no hinter,
/// so a hinted comparison would measure hinting rather than variation.
/// </remarks>
public class NativeVariableFontTests
{
    /// <summary>Straight, curved and diagonal letters, figures, a space, and composites
    /// (accented Latin, which RobotoFlex and DancingScript both build from components).</summary>
    private const string Sample = "AHOQRSWaegkmosy0258&@ ÀÉÎõüÅñçæ";

    private const float Size = 48f;

    /// <summary>
    /// Per-glyph SSIM floor against FreeType. The same outline rendered by the two scanline
    /// fills scores 0.95+ (see <see cref="ScanlineRasterizerSsimTests"/>); a glyph whose
    /// variation is missing or wrong — the default instance compared with wght 1000, say — scores
    /// far lower, which <see cref="Comparison_RejectsAnUnvariedGlyph"/> pins.
    /// </summary>
    private const double SsimFloor = 0.9;

    public static TheoryData<string> RobotoFlexInstances =>
    [
        "wght=100",
        "wght=400",
        "wght=700",
        "wght=1000",
        "wdth=25",
        "wdth=151",
        "opsz=8",
        "opsz=144",
        "slnt=-10",
        "wght=1000,wdth=25",
        "wght=100,wdth=151,opsz=144",
        "wght=700,opsz=36,GRAD=150,YTUC=760,XTRA=603",
    ];

    [Fact]
    public void Capabilities_AdvertiseVariableFonts()
    {
        using var native = new NativeRasterizer();

        native.Capabilities.SupportsVariableFonts.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(RobotoFlexInstances))]
    public void RobotoFlex_MatchesFreeType(string instance)
    {
        var failures = CompareWithFreeType(TestFonts.RobotoFlexBytes(), Axes(instance));

        failures.ShouldBeEmpty($"RobotoFlex {instance} disagrees with FreeType");
    }

    [Fact]
    public void DancingScript_Bold_MatchesFreeType()
    {
        var failures = CompareWithFreeType(TestFonts.DancingScriptBytes(), Axes("wght=700"));

        failures.ShouldBeEmpty("DancingScript wght=700 disagrees with FreeType");
    }

    [Fact]
    public void Comparison_RejectsAnUnvariedGlyph()
    {
        // Guards the comparison itself: Native's default instance against FreeType's heaviest
        // must fail it, otherwise the theory above could pass with variations ignored.
        var bytes = TestFonts.RobotoFlexBytes();
        var options = new RasterOptions { Size = Size, Dpi = 72, EnableHinting = false };

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(FvarAxes(bytes), Axes("wght=1000"));
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);

        var expected = freeType.RasterizeGlyph('H', options)!;
        var actual = native.RasterizeGlyph('H', options)!;

        Ssim(expected, actual).ShouldBeLessThan(SsimFloor);
    }

    [Fact]
    public void VariedGlyph_IsHeavierThanTheDefault()
    {
        var bytes = TestFonts.RobotoFlexBytes();
        var options = new RasterOptions { Size = Size, Dpi = 72 };

        using var regular = new NativeRasterizer();
        regular.LoadFont(bytes);
        using var black = new NativeRasterizer();
        black.LoadFont(bytes);
        black.SetVariationAxes(FvarAxes(bytes), Axes("wght=1000"));

        long regularInk = regular.RasterizeGlyph('H', options)!.BitmapData.Sum(b => (long)b);
        long blackInk = black.RasterizeGlyph('H', options)!.BitmapData.Sum(b => (long)b);

        blackInk.ShouldBeGreaterThan(regularInk * 3 / 2);
    }

    [Fact]
    public void DefaultCoordinates_AreByteIdenticalToNoAxes()
    {
        // Naming every axis at its default must not take a different path from naming none:
        // same outlines, same declared boxes, same advances.
        var bytes = TestFonts.RobotoFlexBytes();
        var axes = FvarAxes(bytes);
        var defaults = axes.ToDictionary(a => a.Tag, a => a.DefaultValue);
        var options = new RasterOptions { Size = 17.5f, Dpi = 72 };

        using var plain = new NativeRasterizer();
        plain.LoadFont(bytes);
        using var explicitDefault = new NativeRasterizer();
        explicitDefault.LoadFont(bytes);
        explicitDefault.SetVariationAxes(axes, defaults);

        foreach (int codepoint in Enumerable.Range(0x20, 0xE0))
        {
            var a = plain.RasterizeGlyph(codepoint, options);
            var b = explicitDefault.RasterizeGlyph(codepoint, options);
            if (a is null)
            {
                b.ShouldBeNull();
                continue;
            }

            b.ShouldNotBeNull();
            b!.Metrics.ShouldBe(a.Metrics, $"U+{codepoint:X4}");
            b.BitmapData.ShouldBe(a.BitmapData, $"U+{codepoint:X4}");
        }

        explicitDefault.GetFontMetrics(options).ShouldBeEquivalentTo(plain.GetFontMetrics(options));
    }

    [Theory]
    [InlineData("wght=1000")]
    [InlineData("wdth=25")]
    [InlineData("opsz=144,wght=100")]
    public void GeneratedFont_FollowsTheInstance_AndItsLineMetricsMatchFreeType(string instance)
    {
        // Through the public pipeline: core only calls SetVariationAxes when the backend
        // advertises SupportsVariableFonts, so this is what a .bmfc variationAxes line reaches.
        var bytes = TestFonts.RobotoFlexBytes();

        FontGeneratorOptions Options(RasterizerBackend backend, string? axes) => new()
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = backend,
            VariationAxes = axes is null ? null : Axes(axes),
        };

        var varied = BmFont.Generate(bytes, Options(RasterizerBackend.Native, instance));
        var plain = BmFont.Generate(bytes, Options(RasterizerBackend.Native, null));
        var freeType = BmFont.Generate(bytes, Options(RasterizerBackend.FreeType, instance));

        varied.GetPngData()[0].ShouldNotBe(plain.GetPngData()[0], "the varied atlas is the default one");

        var nativeCommon = BmFontReader.ReadText(varied.FntText).Common;
        var freeTypeCommon = BmFontReader.ReadText(freeType.FntText).Common;
        nativeCommon.Base.ShouldBe(freeTypeCommon.Base);
        nativeCommon.LineHeight.ShouldBe(freeTypeCommon.LineHeight);
    }

    [Fact]
    public void Mvar_WinAscentAndDescent_FollowTheInstance()
    {
        // RobotoFlex's own MVAR varies only cap/x-height and caret slope, none of which
        // GetFontMetrics reads, so swap in one that moves usWinAscent by +200 and usWinDescent
        // by +100 at wght 1000. At 2048px one font unit is one pixel.
        var bytes = SyntheticFonts.WithTables(
            TestFonts.RobotoFlexBytes(), new Dictionary<string, byte[]?> { ["MVAR"] = BuildMvar() });
        var options = new RasterOptions { Size = 2048, Dpi = 72 };

        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        var initial = native.GetFontMetrics(options)!;

        native.SetVariationAxes(FvarAxes(bytes), Axes("wght=1000"));
        var heaviest = native.GetFontMetrics(options)!;

        native.SetVariationAxes(FvarAxes(bytes), Axes("wght=700"));
        var bold = native.GetFontMetrics(options)!;

        (initial.Ascent, initial.Descent, initial.LineHeight).ShouldBe((2461, 600, 3061));
        (heaviest.Ascent, heaviest.Descent, heaviest.LineHeight).ShouldBe((2661, 700, 3361));
        (bold.Ascent, bold.Descent, bold.LineHeight).ShouldBe((2561, 650, 3211));
    }

    [Fact]
    public void WithoutHvar_AdvancesComeFromPhantomPoints()
    {
        // gvar carries the same advance variation in each glyph's phantom points; with HVAR
        // removed the two must agree to within a font unit (one pixel at 2048px).
        var bytes = TestFonts.RobotoFlexBytes();
        var noHvar = SyntheticFonts.WithTables(bytes, new Dictionary<string, byte[]?> { ["HVAR"] = null });
        var options = new RasterOptions { Size = 2048, Dpi = 72 };
        var axes = Axes("wdth=25,wght=900");

        using var withHvar = new NativeRasterizer();
        withHvar.LoadFont(bytes);
        withHvar.SetVariationAxes(FvarAxes(bytes), axes);
        using var phantom = new NativeRasterizer();
        phantom.LoadFont(noHvar);
        phantom.SetVariationAxes(FvarAxes(noHvar), axes);
        using var plain = new NativeRasterizer();
        plain.LoadFont(bytes);

        var failures = new StringBuilder();
        int changed = 0;
        foreach (char c in Sample)
        {
            int expected = withHvar.GetGlyphMetrics(c, options)!.Value.Advance;
            int actual = phantom.GetGlyphMetrics(c, options)!.Value.Advance;
            if (Math.Abs(expected - actual) > 1)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}': HVAR {expected}, phantom {actual}\n");

            if (actual != plain.GetGlyphMetrics(c, options)!.Value.Advance)
                changed++;
        }

        failures.ToString().ShouldBeEmpty();
        changed.ShouldBeGreaterThan(Sample.Length / 2, "condensed advances barely moved");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Renders <see cref="Sample"/> through both backends at one instance and lists every glyph
    /// whose advance or ink extent differs by more than a pixel, or whose bitmap scores under
    /// <see cref="SsimFloor"/>.
    /// </summary>
    /// <remarks>
    /// Ink extents, not bitmap boxes: FreeType sizes its bitmap from the outline's control box,
    /// while a varied Native outline is measured tightly, so on slanted curves FreeType's box
    /// carries a blank column or two that Native's does not. The ink itself must agree.
    /// </remarks>
    private static string CompareWithFreeType(byte[] bytes, Dictionary<string, float> axes)
    {
        var options = new RasterOptions { Size = Size, Dpi = 72, EnableHinting = false };
        var fvar = FvarAxes(bytes);

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(fvar, axes);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);
        native.SetVariationAxes(fvar, axes);

        var failures = new StringBuilder();
        foreach (char c in Sample)
        {
            var expected = freeType.RasterizeGlyph(c, options);
            var actual = native.RasterizeGlyph(c, options);
            if (expected is null || actual is null)
            {
                if ((expected is null) != (actual is null))
                    failures.Append(CultureInfo.InvariantCulture, $"'{c}': only one backend mapped it\n");
                continue;
            }

            Check(failures, c, "advance", expected.Metrics.Advance, actual.Metrics.Advance);

            var expectedInk = InkBounds(expected);
            var actualInk = InkBounds(actual);
            if (expectedInk is null || actualInk is null)
            {
                if ((expectedInk is null) != (actualInk is null))
                    failures.Append(CultureInfo.InvariantCulture, $"'{c}': only one backend drew ink\n");
                continue;
            }

            Check(failures, c, "ink left", expectedInk.Value.Left, actualInk.Value.Left);
            Check(failures, c, "ink top", expectedInk.Value.Top, actualInk.Value.Top);
            Check(failures, c, "ink right", expectedInk.Value.Right, actualInk.Value.Right);
            Check(failures, c, "ink bottom", expectedInk.Value.Bottom, actualInk.Value.Bottom);

            double ssim = Ssim(expected, actual);
            if (ssim < SsimFloor)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}': SSIM {ssim:F3}\n");
        }

        return failures.ToString();
    }

    private static void Check(StringBuilder failures, char c, string field, int expected, int actual)
    {
        if (Math.Abs(expected - actual) > 1)
            failures.Append(CultureInfo.InvariantCulture, $"'{c}' {field}: FreeType {expected}, native {actual}\n");
    }

    /// <summary>
    /// The extent of a glyph's visible ink relative to the pen and baseline (Y up), ignoring
    /// anti-aliasing dust under an eighth coverage; null for a glyph with none.
    /// </summary>
    private static (int Left, int Top, int Right, int Bottom)? InkBounds(RasterizedGlyph glyph)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                if (glyph.BitmapData[(y * glyph.Pitch) + x] < 32)
                    continue;

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        if (minX == int.MaxValue)
            return null;

        return (glyph.Metrics.BearingX + minX, glyph.Metrics.BearingY - minY,
            glyph.Metrics.BearingX + maxX + 1, glyph.Metrics.BearingY - maxY - 1);
    }

    private static IReadOnlyList<VariationAxis> FvarAxes(byte[] bytes) =>
        BmFont.ReadFontInfo(bytes).VariationAxes ?? throw new InvalidOperationException("not a variable font");

    private static Dictionary<string, float> Axes(string instance) =>
        instance.Split(',').Select(p => p.Split('=')).ToDictionary(
            p => p[0], p => float.Parse(p[1], CultureInfo.InvariantCulture));

    /// <summary>
    /// SSIM of two glyph bitmaps placed on a shared canvas by their bearings, so a one-pixel
    /// box difference is compared as the offset it is rather than as a mismatch everywhere.
    /// </summary>
    private static double Ssim(RasterizedGlyph a, RasterizedGlyph b)
    {
        int left = Math.Min(a.Metrics.BearingX, b.Metrics.BearingX);
        int top = Math.Max(a.Metrics.BearingY, b.Metrics.BearingY);
        int right = Math.Max(a.Metrics.BearingX + a.Width, b.Metrics.BearingX + b.Width);
        int bottom = Math.Min(a.Metrics.BearingY - a.Height, b.Metrics.BearingY - b.Height);
        int width = right - left;
        int height = top - bottom;

        byte[] Place(RasterizedGlyph g)
        {
            var canvas = new byte[width * height];
            int ox = g.Metrics.BearingX - left;
            int oy = top - g.Metrics.BearingY;
            for (int y = 0; y < g.Height; y++)
                Array.Copy(g.BitmapData, y * g.Pitch, canvas, ((oy + y) * width) + ox, g.Width);
            return canvas;
        }

        return ImageSimilarity.Ssim(Place(a), Place(b), width, height);
    }

    /// <summary>
    /// An MVAR whose store has one region (wght peak +1, every other RobotoFlex axis ignored)
    /// moving 'hcla' by +200 and 'hcld' by +100.
    /// </summary>
    private static byte[] BuildMvar()
    {
        const int AxisCount = 13;
        var regionAxes = new List<byte>();
        for (int axis = 0; axis < AxisCount; axis++)
        {
            double peak = axis == 1 ? 1 : 0;   // wght is RobotoFlex's second fvar axis
            regionAxes.AddRange(Be.Concat(Be.F2Dot14(0), Be.F2Dot14(peak), Be.F2Dot14(peak)));
        }

        byte[] regions = Be.Concat(Be.U16(AxisCount), Be.U16(1), regionAxes.ToArray());
        byte[] data = Be.Concat(Be.U16(2), Be.U16(1), Be.U16(1), Be.U16(0), Be.I16(200), Be.I16(100));
        const int StoreHeader = 2 + 4 + 2 + 4;
        byte[] store = Be.Concat(
            Be.U16(1), Be.U32(StoreHeader), Be.U16(1), Be.U32((uint)(StoreHeader + regions.Length)),
            regions, data);

        const int Header = 12;
        const int RecordSize = 8;
        return Be.Concat(
            Be.U16(1), Be.U16(0), Be.U16(0),
            Be.U16(RecordSize), Be.U16(2), Be.U16(Header + (2 * RecordSize)),
            Be.Tag("hcla"), Be.U16(0), Be.U16(0),
            Be.Tag("hcld"), Be.U16(0), Be.U16(1),
            store);
    }
}
