using System.Globalization;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 174 — Native's auto-hinting end to end. Hinting runs for
/// <see cref="AntiAliasMode.Light"/> requests with <see cref="RasterOptions.EnableHinting"/> on:
/// vertical-only (y) grid fitting, so horizontal edges land on pixel boundaries while advances
/// and horizontal positions are untouched.
/// </summary>
public class NativeHintingTests
{
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string GlyphSample = Letters + "0123456789 .,;:!?'\"()[]{}<>@#$%&*-+=/\\|~^";

    public static TheoryData<float> SmallSizes => [12f, 14f, 16f, 18f];

    private static RasterOptions Hinted(float size) => new() { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = true };

    private static RasterOptions Unhinted(float size) => new() { Size = size, AntiAlias = AntiAliasMode.Light, EnableHinting = false };

    [Fact]
    public void Capabilities_AdvertiseLight()
    {
        new NativeRasterizer().Capabilities.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.Light);
    }

    [Fact]
    public void LightWithHintingOff_IsExactlyGrayscale()
    {
        using var native = Load(TestFonts.RobotoRegularBytes());

        foreach (char c in GlyphSample)
        {
            var light = native.RasterizeGlyph(c, Unhinted(14f));
            var gray = native.RasterizeGlyph(c, new RasterOptions { Size = 14f, EnableHinting = false });
            light!.BitmapData.ShouldBe(gray!.BitmapData, $"'{c}'");
            light.Metrics.ShouldBe(gray.Metrics, $"'{c}'");
        }
    }

    [Theory]
    [MemberData(nameof(SmallSizes))]
    public void HorizontalEdges_LandOnPixelBoundaries(float size)
    {
        // 'H' (cap height, baseline), 'z' (x-height, baseline), 'p' (descender, x-height):
        // with the edge on a pixel boundary the outermost row is as solid as the row inside it.
        // Unhinted, at least one of these rows is a faint partial row at every size tested.
        // ('x' is no use here: its diagonals shift columns from row to row.)
        using var native = Load(TestFonts.RobotoRegularBytes());
        var failures = new StringBuilder();

        foreach (char c in "Hzp")
        {
            var glyph = native.RasterizeGlyph(c, Hinted(size))!;
            double top = RowSolidity(glyph, 0, 1);
            double bottom = RowSolidity(glyph, glyph.Height - 1, glyph.Height - 2);
            if (top < 0.9 || bottom < 0.9)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' top {top:F2} bottom {bottom:F2}\n");
        }

        failures.ToString().ShouldBeEmpty($"hinted edges are not crisp at {size}px");
    }

    [Theory]
    [MemberData(nameof(SmallSizes))]
    public void Hinting_ReducesPartialCoverage(float size)
    {
        // Partial pixels (neither empty nor solid) are what makes small text look soft. Grid
        // fitting the horizontals must remove a share of them across the alphabet. Only a share:
        // fitting is vertical-only, so the soft columns along vertical stems, curves and
        // diagonals remain (Roboto: 8-15% fewer partial pixels at these sizes).
        using var native = Load(TestFonts.RobotoRegularBytes());
        double hinted = 0, unhinted = 0;

        foreach (char c in Letters)
        {
            hinted += Fuzz(native.RasterizeGlyph(c, Hinted(size))!);
            unhinted += Fuzz(native.RasterizeGlyph(c, Unhinted(size))!);
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{size}px fuzz: hinted {hinted:F1}, unhinted {unhinted:F1}");
        hinted.ShouldBeLessThan(unhinted * 0.95, $"hinted {hinted:F1} vs unhinted {unhinted:F1} at {size}px");
    }

    [Theory]
    [MemberData(nameof(SmallSizes))]
    public void Hinting_KeepsAdvances_AndMetricsMatchTheBitmap(float size)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var failures = new StringBuilder();

        foreach (char c in GlyphSample)
        {
            var hinted = native.RasterizeGlyph(c, Hinted(size))!;
            var unhinted = native.RasterizeGlyph(c, Unhinted(size))!;
            var measured = native.GetGlyphMetrics(c, Hinted(size))!.Value;

            if (hinted.Metrics.Advance != unhinted.Metrics.Advance)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' advance {hinted.Metrics.Advance} vs {unhinted.Metrics.Advance}\n");
            if (measured != hinted.Metrics || measured.Width != hinted.Width || measured.Height != hinted.Height)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' GetGlyphMetrics {measured} vs bitmap {hinted.Metrics}\n");
        }

        failures.ToString().ShouldBeEmpty($"hinting broke metrics at {size}px");
    }

    [Theory]
    [MemberData(nameof(SmallSizes))]
    public void HintedOutput_IsCloserToHintedFreeType_ThanUnhinted(float size)
    {
        // FreeType hints Roboto with its TrueType instructions; Native's auto-hinter must move
        // the output towards that, not away from it.
        var bytes = TestFonts.RobotoRegularBytes();
        using var native = Load(bytes);
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        var reference = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Grayscale, EnableHinting = true };

        double hinted = 0, unhinted = 0;
        int count = 0;
        foreach (char c in Letters)
        {
            var expected = freeType.RasterizeGlyph(c, reference)!;
            hinted += NativeSyntheticStyleTests.SsimOnSharedCanvas(native.RasterizeGlyph(c, Hinted(size))!, expected);
            unhinted += NativeSyntheticStyleTests.SsimOnSharedCanvas(native.RasterizeGlyph(c, Unhinted(size))!, expected);
            count++;
        }

        hinted /= count;
        unhinted /= count;
        TestContext.Current.TestOutputHelper?.WriteLine($"{size}px mean SSIM vs hinted FreeType: hinted {hinted:F4}, unhinted {unhinted:F4}");
        hinted.ShouldBeGreaterThan(unhinted, $"hinted {hinted:F4} vs unhinted {unhinted:F4} at {size}px");
    }

    [Fact]
    public void Hinting_AppliesToVariableInstances()
    {
        var bytes = TestFonts.RobotoFlexBytes();
        using var native = Load(bytes);
        native.SetVariationAxes(
            BmFont.ReadFontInfo(bytes).VariationAxes!,
            new Dictionary<string, float> { ["wght"] = 700 });

        AssertHintedAndCrisp(native, 'H', 16f);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Hinting_AppliesToSyntheticStyles(bool bold, bool italic)
    {
        using var native = Load(TestFonts.RobotoRegularBytes());
        var hinted = Hinted(16f) with { Bold = bold, Italic = italic };
        var unhinted = Unhinted(16f) with { Bold = bold, Italic = italic };

        var glyph = native.RasterizeGlyph('H', hinted)!;
        glyph.BitmapData.ShouldNotBe(native.RasterizeGlyph('H', unhinted)!.BitmapData);
        RowSolidity(glyph, 0, 1).ShouldBeGreaterThan(0.9);
        glyph.Metrics.Advance.ShouldBe(native.RasterizeGlyph('H', unhinted)!.Metrics.Advance);
    }

    [Fact]
    public void Hinting_AppliesToColorLayers()
    {
        using var native = Load(TestFonts.BungeeColorBytes());
        var hinted = Hinted(16f) with { ColorFont = true };
        var unhinted = Unhinted(16f) with { ColorFont = true };

        var glyph = native.RasterizeGlyph('H', hinted)!;
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        glyph.BitmapData.ShouldContain(b => b != 0);
        glyph.BitmapData.ShouldNotBe(native.RasterizeGlyph('H', unhinted)!.BitmapData);
        native.GetGlyphMetrics('H', hinted)!.Value.ShouldBe(glyph.Metrics);
    }

    private static void AssertHintedAndCrisp(NativeRasterizer native, char c, float size)
    {
        var glyph = native.RasterizeGlyph(c, Hinted(size))!;
        glyph.BitmapData.ShouldNotBe(native.RasterizeGlyph(c, Unhinted(size))!.BitmapData);
        RowSolidity(glyph, 0, 1).ShouldBeGreaterThan(0.9);
        RowSolidity(glyph, glyph.Height - 1, glyph.Height - 2).ShouldBeGreaterThan(0.9);
    }

    private static NativeRasterizer Load(byte[] bytes)
    {
        var native = new NativeRasterizer();
        native.LoadFont(bytes);
        return native;
    }

    /// <summary>
    /// How solid an outermost row is compared with its inner neighbour: the sum of its coverage
    /// over the sum of the inner row's, over the columns the inner row inks. 1 = a crisp edge.
    /// </summary>
    internal static double RowSolidity(RasterizedGlyph glyph, int edgeRow, int innerRow)
    {
        double edge = 0, inner = 0;
        for (int x = 0; x < glyph.Width; x++)
        {
            byte i = glyph.BitmapData[(innerRow * glyph.Pitch) + x];
            if (i == 0)
                continue;
            inner += i;
            edge += glyph.BitmapData[(edgeRow * glyph.Pitch) + x];
        }

        return inner == 0 ? 0 : edge / inner;
    }

    /// <summary>Total partial coverage: each pixel's distance from fully off or fully on, in [0, 1].</summary>
    internal static double Fuzz(RasterizedGlyph glyph)
    {
        double fuzz = 0;
        foreach (byte b in glyph.BitmapData)
            fuzz += Math.Min(b, 255 - b) / 127.5;
        return fuzz;
    }
}
