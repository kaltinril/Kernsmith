using System.Globalization;
using System.Text;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 167 — Native's synthetic bold and italic against FreeType's, which the parity
/// targets are taken from (<c>FT_Outline_Embolden</c> at <c>max(0.5px, ppem/24)</c>, and
/// <c>FT_GlyphSlot_Oblique</c>'s 12° shear).
/// </summary>
/// <remarks>
/// FreeType runs with hinting disabled for the same reason as in
/// <see cref="NativeMetricsAgreementTests"/>: Native has no hinter, and a hinted comparison
/// would measure hinting rather than the styling.
/// </remarks>
public class NativeSyntheticStyleTests
{
    private const string GlyphSample =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,;:!?'\"()[]{}<>@#$%&*-+=/\\|~^";

    public static TheoryData<float, bool, bool> StyledSizes()
    {
        var data = new TheoryData<float, bool, bool>();
        foreach (float size in new[] { 12f, 16f, 24f, 32f, 48f, 96f })
        {
            data.Add(size, true, false);
            data.Add(size, false, true);
            data.Add(size, true, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(StyledSizes))]
    public void StyledGlyphMetrics_AreTheRenderedGlyphs_WithFreeTypesAdvance(float size, bool bold, bool italic)
    {
        // FreeType's GetGlyphMetrics is no reference for styled bearings: FT_GlyphSlot_Oblique
        // leaves the slot metrics unsheared and its embolden moves no bearing, so they stop
        // describing the bitmap it renders. Native's contract is that metrics are exactly what
        // RasterizeGlyph produces; the advance is compared against FreeType.
        var options = Options(size, bold, italic);
        using var freeType = Load(new FreeTypeRasterizer());
        using var native = Load(new NativeRasterizer());

        var failures = new StringBuilder();
        foreach (char character in GlyphSample)
        {
            var expected = freeType.GetGlyphMetrics(character, options);
            if (expected is null)
                continue;

            var measured = native.GetGlyphMetrics(character, options);
            var rendered = native.RasterizeGlyph(character, options);
            if (measured is null || rendered is null)
            {
                failures.Append(CultureInfo.InvariantCulture, $"'{character}': native returned null\n");
                continue;
            }

            if (measured.Value != rendered.Metrics)
                failures.Append(CultureInfo.InvariantCulture, $"'{character}': measured {measured.Value}, rendered {rendered.Metrics}\n");

            Compare(failures, character, "advance", expected.Value.Advance, measured.Value.Advance);
        }

        failures.ToString().ShouldBeEmpty($"styled glyph metrics ({Describe(size, bold, italic)})");
    }

    [Theory]
    [MemberData(nameof(StyledSizes))]
    public void StyledInk_MatchesFreeType_WithinOnePixel(float size, bool bold, bool italic)
    {
        // Compares where the ink lands relative to the pen and baseline, not the bitmap boxes:
        // FreeType sizes its bitmap from the outline's control box, which for sheared or
        // emboldened curves can be a column or two wider than the ink. Native sizes from the
        // tight bounds.
        var options = Options(size, bold, italic);
        using var freeType = Load(new FreeTypeRasterizer());
        using var native = Load(new NativeRasterizer());

        var failures = new StringBuilder();
        foreach (char character in GlyphSample)
        {
            var expected = freeType.RasterizeGlyph(character, options);
            if (expected is null)
                continue;

            var actual = native.RasterizeGlyph(character, options);
            if (actual is null)
            {
                failures.Append(CultureInfo.InvariantCulture, $"'{character}': native returned null\n");
                continue;
            }

            Compare(failures, character, "advance", expected.Metrics.Advance, actual.Metrics.Advance);

            var expectedInk = InkBounds(expected);
            var actualInk = InkBounds(actual);
            if (expectedInk is null || actualInk is null)
            {
                if (expectedInk != actualInk)
                    failures.Append(CultureInfo.InvariantCulture, $"'{character}': ink {actualInk}, FreeType {expectedInk}\n");
                continue;
            }

            var (eLeft, eTop, eRight, eBottom) = expectedInk.Value;
            var (aLeft, aTop, aRight, aBottom) = actualInk.Value;
            Compare(failures, character, "ink left", eLeft, aLeft);
            Compare(failures, character, "ink top", eTop, aTop);
            Compare(failures, character, "ink right", eRight, aRight);
            Compare(failures, character, "ink bottom", eBottom, aBottom);
        }

        failures.ToString().ShouldBeEmpty($"styled ink disagrees with FreeType ({Describe(size, bold, italic)})");
    }

    [Theory]
    [MemberData(nameof(StyledSizes))]
    public void StyledBitmaps_LookLikeFreeTypes(float size, bool bold, bool italic)
    {
        var options = Options(size, bold, italic);
        var unstyled = Options(size, bold: false, italic: false);
        using var freeType = Load(new FreeTypeRasterizer());
        using var native = Load(new NativeRasterizer());

        // Styled glyphs score 0.96-1.0 (the residue is the cubic vs quadratic control polygon
        // the two embolden, plus curve tessellation), while Native's *unstyled* glyph scores at
        // most ~0.82 against FreeType's styled one — so the 0.95 bar cannot be met by
        // silently ignoring the style.
        var failures = new StringBuilder();
        foreach (char character in "AOeBIgW8as")
        {
            var reference = freeType.RasterizeGlyph(character, options)!;
            double styled = SsimOnSharedCanvas(native.RasterizeGlyph(character, options)!, reference);
            double plain = SsimOnSharedCanvas(native.RasterizeGlyph(character, unstyled)!, reference);

            if (styled <= 0.95 || styled <= plain)
                failures.Append(CultureInfo.InvariantCulture, $"'{character}': styled {styled:F4}, unstyled {plain:F4}\n");
        }

        failures.ToString().ShouldBeEmpty($"styled bitmaps differ from FreeType's ({Describe(size, bold, italic)})");
    }

    [Theory]
    [InlineData('o')]
    [InlineData('e')]
    [InlineData('B')]
    public void Bold_KeepsCountersOpen_At12px(char character)
    {
        // At 12px the counters of 'o', 'e' and 'B' are two or three pixels across, and the
        // half-pixel bold eats into them from every side. The counter must stay as open as
        // FreeType's: the darkest-to-lightest coverage along the middle column must still
        // dip to near-empty somewhere inside the ink.
        var options = Options(12f, bold: true, italic: false);
        using var freeType = Load(new FreeTypeRasterizer());
        using var native = Load(new NativeRasterizer());

        int nativeDip = DeepestCounterDip(native.RasterizeGlyph(character, options)!);
        int freeTypeDip = DeepestCounterDip(freeType.RasterizeGlyph(character, options)!);

        nativeDip.ShouldBeLessThan(128, $"bold '{character}' at 12px filled its counter");
        Math.Abs(nativeDip - freeTypeDip).ShouldBeLessThanOrEqualTo(64,
            $"bold '{character}' counter: native {nativeDip}, FreeType {freeTypeDip}");
    }

    /// <summary>
    /// The lowest coverage on the bitmap's middle column strictly between its first and last
    /// inked pixels — the counter, for a glyph with one.
    /// </summary>
    private static int DeepestCounterDip(RasterizedGlyph glyph)
    {
        int x = glyph.Width / 2;
        int first = -1, last = -1;
        for (int y = 0; y < glyph.Height; y++)
        {
            if (glyph.BitmapData[(y * glyph.Pitch) + x] < 128)
                continue;

            if (first < 0)
                first = y;
            last = y;
        }

        int dip = 255;
        for (int y = first + 1; y < last; y++)
            dip = Math.Min(dip, glyph.BitmapData[(y * glyph.Pitch) + x]);

        return dip;
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static RasterOptions Options(float size, bool bold, bool italic) =>
        new() { Size = size, Dpi = 72, EnableHinting = false, Bold = bold, Italic = italic };

    private static IRasterizer Load(IRasterizer rasterizer)
    {
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        return rasterizer;
    }

    private static string Describe(float size, bool bold, bool italic) =>
        string.Create(CultureInfo.InvariantCulture, $"{size}px{(bold ? " bold" : "")}{(italic ? " italic" : "")}");

    /// <summary>
    /// The inked pixel extent relative to the pen (x) and baseline (y, down), exclusive on the
    /// right and bottom; null when nothing is inked.
    /// </summary>
    private static (int Left, int Top, int Right, int Bottom)? InkBounds(RasterizedGlyph glyph)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < glyph.Width; x++)
            {
                if (glyph.BitmapData[(y * glyph.Pitch) + x] == 0)
                    continue;

                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        if (left == int.MaxValue)
            return null;

        int originX = glyph.Metrics.BearingX;
        int originY = -glyph.Metrics.BearingY;
        return (originX + left, originY + top, originX + right, originY + bottom);
    }

    private static void Compare(StringBuilder failures, char character, string field, int expected, int actual)
    {
        if (Math.Abs(expected - actual) <= 1)
            return;

        failures.Append(CultureInfo.InvariantCulture, $"'{character}' {field}: expected {expected}, got {actual}\n");
    }

    /// <summary>
    /// SSIM of two glyph bitmaps placed by their own bearings on one canvas covering both, so
    /// a one-pixel difference in box size compares as ink placement, not as misalignment.
    /// </summary>
    internal static double SsimOnSharedCanvas(RasterizedGlyph a, RasterizedGlyph b)
    {
        int left = Math.Min(a.Metrics.BearingX, b.Metrics.BearingX);
        int top = Math.Min(-a.Metrics.BearingY, -b.Metrics.BearingY);
        int right = Math.Max(a.Metrics.BearingX + a.Width, b.Metrics.BearingX + b.Width);
        int bottom = Math.Max(-a.Metrics.BearingY + a.Height, -b.Metrics.BearingY + b.Height);
        int width = right - left;
        int height = bottom - top;

        return ImageSimilarity.Ssim(Place(a), Place(b), width, height);

        byte[] Place(RasterizedGlyph glyph)
        {
            var canvas = new byte[width * height];
            int ox = glyph.Metrics.BearingX - left;
            int oy = -glyph.Metrics.BearingY - top;
            for (int y = 0; y < glyph.Height; y++)
                Array.Copy(glyph.BitmapData, y * glyph.Pitch, canvas, ((oy + y) * width) + ox, glyph.Width);

            return canvas;
        }
    }
}
