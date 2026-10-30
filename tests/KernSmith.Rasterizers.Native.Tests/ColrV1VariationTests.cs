using System.Globalization;
using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticColrV1;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phases 171 + 172: COLR version 1 values with variation data (VarIndexBase → DeltaSetIndexMap
/// or the implicit mapping → the COLR ItemVariationStore). No committed fixture is a variable
/// COLRv1 font, so RobotoFlex gets a hand-built table whose one region peaks at wght = 1000.
/// </summary>
public class ColrV1VariationTests
{
    private const uint Red = 0xFF0000FFu;

    private static readonly RasterOptions Color48 = new() { Size = 48, ColorFont = true };
    private static readonly RasterOptions Plain48 = new() { Size = 48 };

    private static readonly byte[] Flex = TestFonts.RobotoFlexBytes();
    private static readonly IReadOnlyList<VariationAxis> Axes = BmFont.ReadFontInfo(Flex).VariationAxes!;
    private static readonly int O = NativeFontFace.Load(Flex).GetGlyphIndex('O');
    private static readonly int I = NativeFontFace.Load(Flex).GetGlyphIndex('I');

    [Fact]
    public void VarSolid_Alpha_FollowsTheInstance_WithTheImplicitMapping()
    {
        // varIndexBase 0 → delta set (outer 0, inner 0): −0.5 alpha at the peak.
        var font = Font(Colr(O, new Glyph(O, new Solid(0, 1, Var: 0)), variations: Vars([-8192])));

        using var regular = Load(font);
        using var heavy = Load(font, "wght=1000");

        MaxAlpha(regular.RasterizeGlyph('O', Color48)!).ShouldBe(255);
        MaxAlpha(heavy.RasterizeGlyph('O', Color48)!).ShouldBeInRange(127, 128);
    }

    [Fact]
    public void VarSolid_Alpha_GoesThroughTheDeltaSetIndexMap()
    {
        // varIndexBase 3 → map entry 3 → (0, 1): row 1 is −0.75 alpha; row 0 (unused) is +0.
        var vars = Vars([0], [-12288]) with { IndexMap = [0, 0, 0, 1] };
        var font = Font(Colr(O, new Glyph(O, new Solid(0, 1, Var: 3)), variations: vars));

        using var heavy = Load(font, "wght=1000");
        MaxAlpha(heavy.RasterizeGlyph('O', Color48)!).ShouldBeInRange(63, 65);
    }

    [Fact]
    public void VarTranslate_MovesWithTheInstance()
    {
        // 48px on a 2000-unit em is 0.024 px/unit; 250 units = 6px.
        var font = Font(Colr(O, new Translate(0, 0, new Glyph(I, new Solid(0)), Var: 0), variations: Vars([250], [0])));

        using var heavy = Load(font, "wght=1000");
        var moved = heavy.RasterizeGlyph('O', Color48)!;
        var plainI = heavy.RasterizeGlyph('I', Plain48)!;

        int unitsPerEm = NativeFontFace.Load(Flex).Head.UnitsPerEm;
        int shift = (int)Math.Round(250 * 48.0 / unitsPerEm);
        moved.Metrics.BearingX.ShouldBeInRange(plainI.Metrics.BearingX + shift - 1, plainI.Metrics.BearingX + shift + 1);
        moved.Metrics.BearingY.ShouldBe(plainI.Metrics.BearingY);
        heavy.GetGlyphMetrics('O', Color48).ShouldBe(moved.Metrics);
    }

    [Fact]
    public void VariableClipBox_FollowsTheInstance()
    {
        int unitsPerEm = NativeFontFace.Load(Flex).Head.UnitsPerEm;
        var clip = new Clip(O, O, 0, 0, unitsPerEm / 2, unitsPerEm / 2, Var: 0);
        var font = Font(Colr([(O, new Solid(0))], [clip], Vars([0], [0], [(short)(unitsPerEm / 2)], [0])));

        using var regular = Load(font);
        using var heavy = Load(font, "wght=1000");

        regular.RasterizeGlyph('O', Color48)!.Width.ShouldBe(24);
        heavy.RasterizeGlyph('O', Color48)!.Width.ShouldBe(48);
        heavy.RasterizeGlyph('O', Color48)!.Height.ShouldBe(24);
    }

    private static byte[] Font(byte[] colr) => SyntheticFonts.WithTables(Flex, new Dictionary<string, byte[]?>
    {
        ["COLR"] = colr,
        ["CPAL"] = SyntheticColorTables.Cpal([[Red]]),
    });

    /// <summary>One region peaking at wght = 1000 (normalized +1); each row is one delta set.</summary>
    private static Variations Vars(params short[][] rows)
    {
        int wght = Axes.ToList().FindIndex(a => a.Tag == "wght");
        wght.ShouldBeGreaterThanOrEqualTo(0);
        var region = new (double, double, double)[Axes.Count];
        region[wght] = (0, 1, 1);
        return new Variations(Axes.Count, [region], rows);
    }

    private static NativeRasterizer Load(byte[] font, string? instance = null)
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(font);
        if (instance is not null)
        {
            var values = instance.Split(',').Select(p => p.Split('=')).ToDictionary(
                p => p[0], p => float.Parse(p[1], CultureInfo.InvariantCulture));
            rasterizer.SetVariationAxes(Axes, values);
        }

        return rasterizer;
    }

    private static int MaxAlpha(RasterizedGlyph glyph)
    {
        glyph.Format.ShouldBe(PixelFormat.Rgba32);
        int max = 0;
        for (int i = 3; i < glyph.BitmapData.Length; i += 4)
            max = Math.Max(max, glyph.BitmapData[i]);
        return max;
    }
}
