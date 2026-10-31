using System.Globalization;
using System.Text;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Tests.Goldens;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 179 similarity matrix: for every committed fixture font, anti-alias mode and size, how
/// closely Native's bitmaps (SSIM, mean and worst glyph) and metrics (advance exact; bearings and
/// box within one pixel) agree with FreeType, and with stb_truetype where stb supports the case.
/// Also distance fields, synthetic styles, variation instances and COLR v0 color against FreeType.
/// The full table is written to <c>similarity-matrix.md</c> in the test output folder; the phase
/// 179 doc records it. Floors (<see cref="Floors"/>) sit below the measured values with margin.
/// </summary>
/// <remarks>
/// Deliberately not compared (documented differences, not Native defects):
/// <list type="bullet">
/// <item>MSDF/MTSDF: no other backend renders them.</item>
/// <item>COLR v1 (Reem Kufi Ink with <c>ColorFont</c>): the FreeType backend renders those glyphs as
/// grayscale; <c>NativeColorFontTests</c> compares their silhouettes instead.</item>
/// <item>stb_truetype SDF: a different encoding (128 on edge, 64 levels per pixel, 5 px padding)
/// from FreeType's and Native's; stb Light/Lcd: stb renders both as grayscale, while Native hints
/// Light and renders Lcd as subpixels; stb and CFF2 or variation instances: stb reads neither.</item>
/// </list>
/// Compared with an adjustment:
/// <list type="bullet">
/// <item>Light: both sides hinted, but FreeType runs the font's own hinting (TrueType bytecode,
/// the CFF hinter) while Native auto-hints y only, so stems and heights land on different rows —
/// its floors are the loosest. Every other FreeType comparison is unhinted (Native hints only Light).</item>
/// <item>Lcd: the FreeType backend returns its raw 3-bytes-per-pixel LCD buffer labelled
/// <c>Grayscale8</c> (three times as wide); it is split into R, G and B planes and its width
/// divided by three before comparing.</item>
/// <item>None: FreeType's mono renderer samples pixel centres, Native thresholds coverage at 50%;
/// both run dropout control, so edge pixels flip and single-pixel strokes shift.</item>
/// <item>Color: FreeType's color glyphs are premultiplied, Native's straight, so Native's are
/// premultiplied first and every channel is scored (the worst channel counts).</item>
/// </list>
/// </remarks>
public class NativeSimilarityMatrixTests
{
    /// <summary>Printable ASCII plus a few composites; Reem Kufi Ink adds Arabic.</summary>
    private const string Ascii = "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~ÀÅçéñö";

    private const string ArabicSample = "ابجدعكلمنهوي١٤؟ء";

    /// <summary>Distance-field rows use the shorter golden sample: FreeType's SDF renderer is
    /// slow (Native's is 5-8x faster), and it costs most of the matrix's time.</summary>
    private const string SdfSample = " !%&,.0123?@ABEGHMOQRSWagjmoyçéÅ";

    /// <summary>The fixture fonts compared. RobotoFlex-Latin is left out: it is a subset of
    /// RobotoFlex-Variable with the same outlines, and measured identical to it row for row.</summary>
    private static IEnumerable<GoldenFont> Fonts => GoldenCatalog.Fonts.Where(f => f != GoldenCatalog.RobotoFlexLatin);

    [Fact]
    public void Matrix_MeetsTheFloors()
    {
        // Rows are independent (each owns its rasterizers; FreeType instances own their library).
        // Half the cores: xUnit runs other test classes alongside, some with time budgets.
        var results = Rows().AsParallel().AsOrdered()
            .WithDegreeOfParallelism(Math.Max(1, Environment.ProcessorCount / 2))
            .Select(Measure).ToList();

        var table = new StringBuilder();
        table.Append("| Font | Case | vs | Size | Glyphs | SSIM mean | SSIM min (glyph) | Advance exact | Bearings ±1 | Box ±1 |\n");
        table.Append("|---|---|---|---:|---:|---:|---:|---:|---:|---:|\n");
        foreach (var r in results)
        {
            table.Append(CultureInfo.InvariantCulture,
                $"| {r.Row.Font.Id} | {r.Row.Case} | {r.Row.Reference} | {r.Row.Size} | {r.Glyphs} | {r.Mean:F3} | {r.Min:F3} ({r.Worst}) | {r.AdvanceExact:P0} | {r.BearingsWithin1:P0} | {r.BoxWithin1:P0} |\n");
        }

        string path = Path.Combine(AppContext.BaseDirectory, "similarity-matrix.md");
        File.WriteAllText(path, table.ToString());
        TestContext.Current.TestOutputHelper?.WriteLine(table.ToString());

        var failures = new StringBuilder();
        foreach (var r in results)
        {
            var floor = Floors(r.Row);
            if (r.Glyphs < 10)
                failures.Append(CultureInfo.InvariantCulture, $"{r.Row}: only {r.Glyphs} glyphs compared\n");
            if (r.Mean < floor.Mean)
                failures.Append(CultureInfo.InvariantCulture, $"{r.Row}: mean SSIM {r.Mean:F3} < {floor.Mean}\n");
            if (r.Min < floor.Min)
                failures.Append(CultureInfo.InvariantCulture, $"{r.Row}: min SSIM {r.Min:F3} ({r.Worst}) < {floor.Min}\n");
            if (r.AdvanceExact < floor.Advance)
                failures.Append(CultureInfo.InvariantCulture, $"{r.Row}: advance exact {r.AdvanceExact:P1} < {floor.Advance:P0}\n");
            if (r.BearingsWithin1 < floor.Bearings)
                failures.Append(CultureInfo.InvariantCulture, $"{r.Row}: bearings ±1 {r.BearingsWithin1:P1} < {floor.Bearings:P0}\n");
            if (r.BoxWithin1 < floor.Box)
                failures.Append(CultureInfo.InvariantCulture, $"{r.Row}: box ±1 {r.BoxWithin1:P1} < {floor.Box:P0}\n");
            if (r.Mismatched > 0)
                failures.Append(CultureInfo.InvariantCulture, $"{r.Row}: {r.Mismatched} glyphs mapped or formatted differently ({r.MismatchedList})\n");
        }

        failures.ToString().ShouldBeEmpty($"similarity matrix below its floors; full table: {path}");
    }

    [Fact]
    public void Comparison_ScoresTheWrongGlyphLow()
    {
        // Guards the metric: 'o' against 'e' (same font, size, backend) must score well under
        // every floor that matters, or the matrix would pass anything.
        using var native = new NativeRasterizer();
        native.LoadFont(GoldenCatalog.Roboto.Bytes());
        var options = new RasterOptions { Size = 24, EnableHinting = false };
        double score = Score(native.RasterizeGlyph('o', options)!, native.RasterizeGlyph('e', options)!, lcdReference: false);
        score.ShouldBeLessThan(0.8);
    }

    // ---- Rows ---------------------------------------------------------------------------------

    internal enum Backend
    {
        FreeType,
        Stb,
    }

    internal sealed record Row(GoldenFont Font, string Case, Backend Reference, float Size, RasterOptions Options, Dictionary<string, float>? Axes = null, int Palette = 0)
    {
        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Font.Id} {Case} {Size}px vs {Reference}");
    }

    internal sealed record Result(Row Row, int Glyphs, double Mean, double Min, string Worst, double AdvanceExact, double BearingsWithin1, double BoxWithin1, int Mismatched, string MismatchedList);

    private static IEnumerable<Row> Rows()
    {
        foreach (var font in Fonts)
        {
            foreach (var mode in GoldenCatalog.Modes)
            {
                foreach (float size in GoldenCatalog.Sizes)
                {
                    // Native hints Light only; every other FreeType comparison is unhinted.
                    bool hinted = mode == AntiAliasMode.Light;
                    yield return new Row(font, Name(mode), Backend.FreeType, size,
                        new RasterOptions { Size = size, AntiAlias = mode, EnableHinting = hinted });

                    // stb: plain grayscale and mono only, and not CFF2 (see remarks).
                    if (mode is AntiAliasMode.Grayscale or AntiAliasMode.None && font != GoldenCatalog.SourceCode)
                        yield return new Row(font, Name(mode), Backend.Stb, size, new RasterOptions { Size = size, AntiAlias = mode, EnableHinting = false });
                }
            }
        }

        foreach (var font in Fonts)
        {
            foreach (float size in new[] { 16f, 32f })
                yield return new Row(font, "sdf", Backend.FreeType, size, new RasterOptions { Size = size, Sdf = true, EnableHinting = false });

            foreach (float size in new[] { 16f, 48f })
            {
                yield return new Row(font, "bold", Backend.FreeType, size, new RasterOptions { Size = size, Bold = true, EnableHinting = false });
                yield return new Row(font, "italic", Backend.FreeType, size, new RasterOptions { Size = size, Italic = true, EnableHinting = false });
                yield return new Row(font, "bold-italic", Backend.FreeType, size, new RasterOptions { Size = size, Bold = true, Italic = true, EnableHinting = false });
            }
        }

        foreach (var (font, axes) in new[]
        {
            (GoldenCatalog.RobotoFlex, "wght=100"),
            (GoldenCatalog.RobotoFlex, "wght=900"),
            (GoldenCatalog.SourceCode, "wght=900"),
            (GoldenCatalog.DancingScript, "wght=700"),
        })
        {
            foreach (float size in new[] { 12f, 24f, 48f })
                yield return new Row(font, axes, Backend.FreeType, size, new RasterOptions { Size = size, EnableHinting = false }, GoldenCatalog.Axes(axes));
        }

        foreach (int palette in new[] { 0, 6 })
        {
            foreach (float size in new[] { 16f, 48f, 96f })
            {
                yield return new Row(GoldenCatalog.Bungee, $"color palette {palette}", Backend.FreeType, size,
                    new RasterOptions { Size = size, ColorFont = true, ColorPaletteIndex = palette, EnableHinting = false }, Palette: palette);
            }
        }
    }

    // ---- Measuring ----------------------------------------------------------------------------

    private static Result Measure(Row row)
    {
        var bytes = row.Font.Bytes();
        using var native = new NativeRasterizer();
        using IRasterizer reference = row.Reference == Backend.FreeType ? new FreeTypeRasterizer() : new StbTrueTypeRasterizer();
        native.LoadFont(bytes);
        reference.LoadFont(bytes);
        if (row.Axes is not null)
        {
            var fvar = BmFont.ReadFontInfo(bytes).VariationAxes!;
            native.SetVariationAxes(fvar, row.Axes);
            reference.SetVariationAxes(fvar, row.Axes);
        }

        if (row.Palette != 0)
        {
            native.SelectColorPalette(row.Palette);
            reference.SelectColorPalette(row.Palette);
        }

        bool lcd = row.Options.AntiAlias == AntiAliasMode.Lcd && !row.Options.Sdf;
        string sample = (row.Options.Sdf ? SdfSample : Ascii) + (row.Font == GoldenCatalog.ReemKufi ? ArabicSample : string.Empty);
        int compared = 0, scored = 0, advanceExact = 0, bearings = 0, boxes = 0, inked = 0;
        double total = 0, min = 1;
        string worst = "-";
        var mismatched = new List<string>();

        foreach (char c in sample)
        {
            var mine = native.RasterizeGlyph(c, row.Options);
            var theirs = reference.RasterizeGlyph(c, row.Options);
            if (mine is null && theirs is null)
                continue;

            if (mine is null || theirs is null)
            {
                mismatched.Add($"'{c}' mapped only by {(mine is null ? row.Reference.ToString() : "Native")}");
                continue;
            }

            compared++;
            int theirWidth = lcd ? theirs.Width / 3 : theirs.Width;
            if (mine.Metrics.Advance == theirs.Metrics.Advance)
                advanceExact++;

            bool mineBlank = mine.Width == 0 || mine.Height == 0;
            bool theirsBlank = theirWidth == 0 || theirs.Height == 0;
            if (mineBlank && theirsBlank)
                continue;

            inked++;
            if (!mineBlank && !theirsBlank)
            {
                if (Math.Abs(mine.Metrics.BearingX - theirs.Metrics.BearingX) <= 1 && Math.Abs(mine.Metrics.BearingY - theirs.Metrics.BearingY) <= 1)
                    bearings++;
                if (Math.Abs(mine.Width - theirWidth) <= 1 && Math.Abs(mine.Height - theirs.Height) <= 1)
                    boxes++;
            }

            if (!lcd && mine.Format != theirs.Format)
            {
                mismatched.Add($"'{c}' {mine.Format} vs {theirs.Format}");
                continue;
            }

            double score = mineBlank || theirsBlank ? 0 : Score(mine, theirs, lcd);
            total += score;
            scored++;
            if (score < min)
            {
                min = score;
                worst = c.ToString();
            }
        }

        return new Result(row, compared, scored == 0 ? 0 : total / scored, min, worst,
            Ratio(advanceExact, compared), Ratio(bearings, inked), Ratio(boxes, inked), mismatched.Count, string.Join(", ", mismatched));
    }

    private static double Ratio(int count, int of) => of == 0 ? 1 : (double)count / of;

    /// <summary>SSIM of two glyphs on a canvas covering both, anchored at the pen. RGBA is scored
    /// per channel (Native premultiplied first) and LCD per subpixel plane; the result is the
    /// worst channel for color and the mean of R, G and B for LCD.</summary>
    private static double Score(RasterizedGlyph mine, RasterizedGlyph theirs, bool lcdReference)
    {
        if (lcdReference)
        {
            double sum = 0;
            for (int ch = 0; ch < 3; ch++)
                sum += PlaneSsim(Plane(mine, ch, 4), Plane(theirs, ch, 3));

            return sum / 3;
        }

        if (mine.Format == PixelFormat.Rgba32)
            return ColorComparison.MinimumChannelSsim(ColorComparison.Premultiply(mine), theirs);

        return PlaneSsim(Plane(mine, 0, 1), Plane(theirs, 0, 1));
    }

    private sealed record GlyphPlane(byte[] Data, int Width, int Height, int Left, int Top);

    /// <summary>One channel of a glyph: <paramref name="stride"/> bytes per pixel, so 3 splits
    /// the FreeType backend's LCD buffer and 4 an RGBA glyph.</summary>
    private static GlyphPlane Plane(RasterizedGlyph glyph, int channel, int stride)
    {
        int width = glyph.Width / (stride == 3 ? 3 : 1);
        var data = new byte[width * glyph.Height];
        for (int y = 0; y < glyph.Height; y++)
        {
            for (int x = 0; x < width; x++)
                data[(y * width) + x] = glyph.BitmapData[(y * glyph.Pitch) + (x * stride) + channel];
        }

        return new GlyphPlane(data, width, glyph.Height, glyph.Metrics.BearingX, glyph.Metrics.BearingY);
    }

    private static double PlaneSsim(GlyphPlane a, GlyphPlane b)
    {
        int left = Math.Min(a.Left, b.Left);
        int top = Math.Max(a.Top, b.Top);
        int right = Math.Max(a.Left + a.Width, b.Left + b.Width);
        int bottom = Math.Min(a.Top - a.Height, b.Top - b.Height);
        int width = right - left, height = top - bottom;
        return ImageSimilarity.Ssim(
            ImageSimilarity.Place(a.Data, a.Width, a.Height, a.Left, a.Top, left, top, width, height),
            ImageSimilarity.Place(b.Data, b.Width, b.Height, b.Left, b.Top, left, top, width, height),
            width, height);
    }

    // ---- Floors -------------------------------------------------------------------------------

    internal sealed record Floor(double Mean, double Min, double Advance, double Bearings, double Box);

    /// <summary>
    /// Floors per row, set under the measured matrix (phase 179 doc) with margin. Mono and Light
    /// rows are held to their mean only: single-pixel strokes that land one row or column apart
    /// score near or below zero on their own, so a worst-glyph floor would measure noise.
    /// </summary>
    /// <remarks>
    /// Advances: stb agrees exactly except at exact half-pixel ties, which the StbTrueType
    /// backend rounds to even and Native, like FreeType, rounds up (Roboto Flex 'e' at 16px is
    /// 8.5 px; two such glyphs in a row of 100). FreeType rounds a scaled advance to 26.6 fixed
    /// point before rounding to whole pixels, so an advance a hair under half a pixel (Roboto's
    /// digits at 8px: 1151 units = 4.496 px) rounds up there and down in Native and stb — hence
    /// 86% at 8px. Hinted Light FreeType also moves advances (Native keeps them unhinted).
    /// Boxes: Native crops mono glyphs to their ink, stb keeps the anti-aliased box (None vs
    /// stb); FreeType sizes a slanted glyph from its control box, Native from the tight outline
    /// (italic at 48px).
    /// </remarks>
    private static Floor Floors(Row row)
    {
        int s = (int)row.Size;
        double advance = s <= 8 ? 0.80 : 0.90;
        return (row.Case, row.Reference) switch
        {
            ("none", Backend.FreeType) => new(MeanBySize(s, 0.40, 0.60, 0.70, 0.82, 0.92, 0.94), -1, advance, 0.97, 0.90),
            ("none", Backend.Stb) => new(MeanBySize(s, 0.10, 0.40, 0.60, 0.80, 0.92, 0.95), -1, 0.95, 0.95, 0.30),
            ("light", _) => new(MeanBySize(s, 0.40, 0.60, 0.65, 0.75, 0.80, 0.85), -1, 0.50, 0.88, 0.60),
            ("grayscale", Backend.Stb) => new(0.98, 0.70, 0.95, 0.98, 0.98),
            ("grayscale", _) or ("lcd", _) => new(0.98, 0.80, advance, 0.98, 0.98),
            ("sdf", _) => new(0.98, 0.90, 0.85, 0.98, 0.98),
            ("bold" or "italic" or "bold-italic", _) => new(0.98, 0.70, 0.90, 0.80, 0.70),
            _ when row.Case.StartsWith("color", StringComparison.Ordinal) => new(0.98, 0.90, 0.90, 0.98, 0.98),
            _ when row.Case.StartsWith("wght", StringComparison.Ordinal) => new(0.98, 0.80, 0.90, 0.98, 0.98),
            _ => throw new InvalidOperationException($"no floor for {row}"),
        };

        static double MeanBySize(int size, double at8, double at12, double at16, double at24, double at48, double at96) => size switch
        {
            <= 8 => at8,
            <= 12 => at12,
            <= 16 => at16,
            <= 24 => at24,
            <= 48 => at48,
            _ => at96,
        };
    }

    private static string Name(AntiAliasMode mode) => mode.ToString().ToLowerInvariant();
}
