using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// The kerning bake (<see cref="RasterOptions.BakeHintingIntoKerning"/>) on Native: under
/// <see cref="HintLevel.Normal"/> every advance stays the font's and each glyph is placed by
/// side, so a few pairs come out with no white between their inks. The backend reports exactly
/// those pairs — opened to one column, balanced where it can by closing a pair of the same
/// glyph that has white to spare — for core to fold into the kerning pairs. On by default
/// whenever Native hints at Normal, off at other levels, explicit false honoured, never for a
/// fixed-pitch face.
/// </summary>
public class NativeKerningBakeTests
{
    private const string Ascii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    /// <summary>Roboto's em size for a BMFont cell, 2048 / (2146 + 555) per pixel of cell.</summary>
    private static float RobotoCell(float cell) => cell * 2048f / (2146f + 555f);

    private static RasterOptions Normal(float size, AntiAliasMode mode = AntiAliasMode.Grayscale, bool? bake = null) =>
        new() { Size = size, AntiAlias = mode, HintLevel = HintLevel.Normal, BakeHintingIntoKerning = bake };

    [Fact]
    public void On_ByDefault_UnderNormal_OffWhenToldAndAtOtherLevels()
    {
        using var native = Load();
        var codepoints = Ascii.Select(c => (int)c).ToList();

        native.RasterizeAll(codepoints, Normal(RobotoCell(12), AntiAliasMode.None));
        native.GetKerningAdjustments(Normal(RobotoCell(12), AntiAliasMode.None)).ShouldNotBeNull("on by default under Normal");

        native.RasterizeAll(codepoints, Normal(RobotoCell(12), AntiAliasMode.None, bake: false));
        native.GetKerningAdjustments(Normal(RobotoCell(12), AntiAliasMode.None, bake: false)).ShouldBeNull("explicit false");

        var light = new RasterOptions { Size = RobotoCell(12), HintLevel = HintLevel.Light, BakeHintingIntoKerning = true };
        native.RasterizeAll(codepoints, light);
        native.GetKerningAdjustments(light).ShouldBeNull("only Normal places glyphs on the grid");
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(12f)]
    [InlineData(14f)]
    public void Bilevel_OpensExactlyThePairsWithNoColumnBetweenTheirInks(float cell)
    {
        // Bilevel ink is whole columns: two inks with no column between them touch, whatever
        // their shapes. Every such pair (two digits and pairs with a blank glyph aside) is
        // opened to one column, and no pair with white already is opened.
        using var native = Load();
        var options = Normal(RobotoCell(cell), AntiAliasMode.None);
        var glyphs = native.RasterizeAll(Ascii.Select(c => (int)c).ToList(), options).ToDictionary(g => g.Codepoint);
        var adjustments = native.GetKerningAdjustments(options)!.ToDictionary(a => (a.LeftCodepoint, a.RightCodepoint), a => a.Amount);

        foreach (var (left, right, gap) in Pairs(glyphs))
        {
            float opened = adjustments.GetValueOrDefault((left, right));
            if (gap <= 0 && !(char.IsAsciiDigit((char)left) && char.IsAsciiDigit((char)right)))
                opened.ShouldBe(1 - gap, $"'{(char)left}{(char)right}' gap {gap} at {cell} px");
            else
                opened.ShouldBeLessThanOrEqualTo(0f, $"'{(char)left}{(char)right}' gap {gap} at {cell} px is not opened");
        }
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(12f)]
    [InlineData(14f)]
    public void Grayscale_OpensExactlyThePairsWhoseSolidFacingColumnsMeet(float cell)
    {
        // In grayscale a pair with no column between its boxes fuses only where the two facing
        // columns are both solid ink (three-quarter coverage or more) on a shared row: two stems
        // or two bowl walls in whole columns ("oo", "on" at 10 and 12 px), not a bowl's fringe
        // or a diagonal's foot against a stem ("vo", "co": the facing coverage is under a
        // quarter), which still read as two letters. Recomputed here from the bitmaps.
        using var native = Load();
        var options = Normal(RobotoCell(cell));
        var glyphs = native.RasterizeAll(Ascii.Select(c => (int)c).ToList(), options).ToDictionary(g => g.Codepoint);
        var adjustments = native.GetKerningAdjustments(options)!.ToDictionary(a => (a.LeftCodepoint, a.RightCodepoint), a => a.Amount);

        int opened = 0;
        foreach (var (left, right, gap) in Pairs(glyphs))
        {
            float amount = adjustments.GetValueOrDefault((left, right));
            bool digits = char.IsAsciiDigit((char)left) && char.IsAsciiDigit((char)right);
            if (gap <= 0 && !digits && SolidColumnsMeet(glyphs[left], glyphs[right]))
            {
                amount.ShouldBe(1 - gap, $"'{(char)left}{(char)right}' gap {gap} at {cell} px");
                opened++;
            }
            else
            {
                amount.ShouldBeLessThanOrEqualTo(0f, $"'{(char)left}{(char)right}' gap {gap} at {cell} px is not opened");
            }
        }

        opened.ShouldBeGreaterThan(0);
        adjustments.GetValueOrDefault(('v', 'o')).ShouldBeLessThanOrEqualTo(0f, "a diagonal's foot against a bowl is not a fused pair");
    }

    /// <summary>True when the left glyph's last column and the right glyph's first column are both at least three-quarters covered on some shared row.</summary>
    private static bool SolidColumnsMeet(RasterizedGlyph left, RasterizedGlyph right)
    {
        for (int y = left.Metrics.BearingY; y > left.Metrics.BearingY - left.Height; y--)
        {
            int rightRow = right.Metrics.BearingY - y;
            if (rightRow < 0 || rightRow >= right.Height)
                continue;
            if (left.BitmapData[((left.Metrics.BearingY - y) * left.Pitch) + left.Width - 1] >= 191 && right.BitmapData[rightRow * right.Pitch] >= 191)
                return true;
        }

        return false;
    }

    [Fact]
    public void Compensation_ClosesOnlyPairsWithSlack_AndNoMoreThanWasOpened()
    {
        using var native = Load();
        var options = Normal(RobotoCell(12), AntiAliasMode.None);
        var glyphs = native.RasterizeAll(Ascii.Select(c => (int)c).ToList(), options).ToDictionary(g => g.Codepoint);
        var adjustments = native.GetKerningAdjustments(options)!;

        adjustments.ShouldAllBe(a => a.Amount != 0f);
        adjustments.ShouldNotContain(a => char.IsAsciiDigit((char)a.LeftCodepoint) && char.IsAsciiDigit((char)a.RightCodepoint));
        adjustments.ShouldNotContain(a => a.LeftCodepoint == ' ' || a.RightCodepoint == ' ');
        foreach (var a in adjustments.Where(a => a.Amount < 0f))
        {
            a.Amount.ShouldBe(-1f);
            Gap(glyphs[a.LeftCodepoint], glyphs[a.RightCodepoint]).ShouldBeGreaterThanOrEqualTo(2, $"'{(char)a.LeftCodepoint}{(char)a.RightCodepoint}'");
        }

        foreach (var byLeft in adjustments.GroupBy(a => a.LeftCodepoint))
        {
            float opened = byLeft.Where(a => a.Amount > 0f).Sum(a => a.Amount);
            float closed = -byLeft.Where(a => a.Amount < 0f).Sum(a => a.Amount);
            closed.ShouldBeLessThanOrEqualTo(opened, $"'{(char)byLeft.Key}'");
        }

        adjustments.Count(a => a.Amount < 0f).ShouldBeGreaterThan(0, "some opened pixel is taken back from a pair with slack");
    }

    [Fact]
    public void Bilevel_BakesWhereTheGaspTableWithholdsTheFit()
    {
        // Verdana at a 10 px cell is 8 ppem, where its gasp table says not to grid-fit: the glyphs
        // are left unfitted (threshold-rendered at the integer ppem) but set in whole columns all
        // the same, so the pairs that touch there are as real as a fitted face's and the bake
        // opens them. A withheld fit used to record no sides, and the bake returned nothing.
        var bytes = NativeBytecodeFontTests.SystemFont("verdana.ttf");
        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        var face = native.Face!;
        var options = Normal(10f * face.Head.UnitsPerEm / (face.Os2.WinAscent + (float)face.Os2.WinDescent), AntiAliasMode.None);

        var glyphs = native.RasterizeAll(Ascii.Select(c => (int)c).ToList(), options).ToDictionary(g => g.Codepoint);
        var adjustments = native.GetKerningAdjustments(options).ShouldNotBeNull("the bake runs for a withheld fit")
            .ToDictionary(a => (a.LeftCodepoint, a.RightCodepoint), a => a.Amount);

        var touching = new List<string>();
        for (char left = 'a'; left <= 'z'; left++)
        {
            for (char right = 'a'; right <= 'z'; right++)
            {
                if (Gap(glyphs[left], glyphs[right]) + adjustments.GetValueOrDefault((left, right)) < 1f)
                    touching.Add($"{left}{right}");
            }
        }

        touching.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(16)]
    public void Generate_Bilevel_NoLowercasePairTouches_AfterKerning(int cell)
    {
        // The whole contract through BmFont.Generate, Roboto at 7.6-12.1 ppem: with the bake on
        // by default, every lowercase pair has a column of white between its inks once the written
        // kerning is applied.
        var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), new FontGeneratorOptions
        {
            Size = cell,
            Characters = CharacterSet.FromChars(Ascii),
            AntiAlias = AntiAliasMode.None,
            Kerning = true,
            Rasterizer = new NativeRasterizer(),
        });
        var chars = result.Model.Characters.ToDictionary(c => c.Id);
        var kerning = result.Model.KerningPairs.ToDictionary(k => (k.First, k.Second), k => k.Amount);

        var touching = new List<string>();
        for (char left = 'a'; left <= 'z'; left++)
        {
            for (char right = 'a'; right <= 'z'; right++)
            {
                int gap = chars[left].XAdvance - chars[left].XOffset - chars[left].Width + chars[right].XOffset + kerning.GetValueOrDefault((left, right));
                if (gap < 1)
                    touching.Add($"{left}{right}");
            }
        }

        touching.ShouldBeEmpty($"{cell} px");
    }

    [Fact]
    public void FixedPitch_IsNeverKerned()
    {
        using var native = new NativeRasterizer();
        native.LoadFont(File.ReadAllBytes(TestFonts.SourceCodeVariableOtfPath));
        var options = Normal(12f, AntiAliasMode.None, bake: true);

        native.RasterizeAll(Ascii.Select(c => (int)c).ToList(), options);

        native.GetKerningAdjustments(options).ShouldBeNull();
    }

    [Fact]
    public void Generate_WritesTheBakedPairs_ByDefault()
    {
        // Through BmFont.Generate with no bake setting: Native hints at Normal, so the bake is
        // on and pairs the font does not kern appear; with the bake off they do not.
        static FontGeneratorOptions Options(bool? bake) => new()
        {
            Size = 12,
            Characters = CharacterSet.FromChars(Ascii),
            HintLevel = HintLevel.Normal,
            AntiAlias = AntiAliasMode.None,
            Kerning = true,
            BakeHintingIntoKerning = bake,
            Rasterizer = new NativeRasterizer(),
        };

        var baked = BmFont.Generate(TestFonts.RobotoRegularBytes(), Options(null));
        var plain = BmFont.Generate(TestFonts.RobotoRegularBytes(), Options(false));

        baked.Model.KerningPairs.Count.ShouldBeGreaterThan(plain.Model.KerningPairs.Count);
        baked.Model.KerningPairs.ShouldContain(p => p.Amount > 0);
        baked.FntText.ShouldContain("kerning first=", Case.Sensitive);
    }

    /// <summary>Whole columns of white between a pair's inks, from the rendered boxes.</summary>
    private static int Gap(RasterizedGlyph left, RasterizedGlyph right) =>
        (left.Metrics.Advance - left.Metrics.BearingX - left.Width) + right.Metrics.BearingX;

    private static IEnumerable<(int Left, int Right, int Gap)> Pairs(Dictionary<int, RasterizedGlyph> glyphs)
    {
        foreach (var left in glyphs.Values.Where(g => g.Width > 0))
        {
            foreach (var right in glyphs.Values.Where(g => g.Width > 0))
                yield return (left.Codepoint, right.Codepoint, Gap(left, right));
        }
    }

    private static NativeRasterizer Load()
    {
        var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        return native;
    }
}
