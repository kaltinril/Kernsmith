using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Blank glyphs under <see cref="HintLevel.Normal"/> at text sizes. The letters keep the font's
/// advances, rounded once, and in bilevel sit in whole columns with one column of white between
/// them; a space rounded to nearest the same way (Roboto's 2.26 px at a 12 px cell → 2) left a
/// word gap one column wider than a letter gap, "lazy dogs" reading as "lazydogs". Blank glyphs
/// round up instead, in both anti-alias modes, and in bilevel a blank at least as wide as the
/// space is never under two columns (1.5 letter gaps, rounded up). Unhinted, Light, sizes past
/// the taper's start and fixed-pitch faces keep the font's rounding.
/// </summary>
public class NativeWordSpacingTests
{
    private const string Pangram = "The quick brown fox jumps over the 13 lazy dogs.";

    /// <summary>Roboto's em size for a BMFont cell, 2048 / (2146 + 555) per pixel of cell.</summary>
    private static float RobotoCell(float cell) => cell * 2048f / (2146f + 555f);

    private static RasterOptions Options(float size, AntiAliasMode mode, HintLevel level = HintLevel.Normal) =>
        new() { Size = size, AntiAlias = mode, HintLevel = level };

    [Theory]
    [InlineData(AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Grayscale)]
    public void Space_RoundsUp_UnderNormal_AtTextSizes(AntiAliasMode mode)
    {
        // A 12 px cell is 9.1 ppem, where Roboto's space (508/2048 em) is 2.26 px.
        using var native = Load();
        float size = RobotoCell(12);

        native.RasterizeGlyph(' ', Options(size, mode))!.Metrics.Advance.ShouldBe(3, "Normal rounds the space up");
        native.GetGlyphMetrics(' ', Options(size, mode))!.Value.Advance.ShouldBe(3, "metrics agree with rendering");
        native.RasterizeGlyph(' ', Options(size, mode)).ShouldNotBeNull().Metrics.Advance.ShouldBe(3, "every blank glyph, not only the space");
        native.RasterizeGlyph(' ', Options(size, mode, HintLevel.None))!.Metrics.Advance.ShouldBe(2, "unhinted keeps the font's rounding");
        native.RasterizeGlyph(' ', Options(size, mode, HintLevel.Light))!.Metrics.Advance.ShouldBe(2, "Light fits y only and leaves advances alone");
    }

    [Fact]
    public void Space_KeepsTheFontsRounding_PastTheTapersStart()
    {
        // A 28 px cell is 21.2 ppem: the space is 5.27 px, which rounds to 5 and would round up
        // to 6. The word-gap rounding ends with the taper's start (16 ppem), like the x fit it
        // compensates for.
        using var native = Load();
        native.RasterizeGlyph(' ', Options(RobotoCell(28), AntiAliasMode.None))!.Metrics.Advance.ShouldBe(5);
        native.RasterizeGlyph(' ', Options(RobotoCell(28), AntiAliasMode.Grayscale))!.Metrics.Advance.ShouldBe(5);
    }

    [Fact]
    public void Bilevel_SpaceIsNeverUnderTwoColumns()
    {
        // At 4 ppem Roboto's space is 0.99 px. A bilevel letter gap is one column, so a one-column
        // space is a letter gap, not a word gap; grayscale takes only the rounded-up pixel.
        using var native = Load();
        native.RasterizeGlyph(' ', Options(4f, AntiAliasMode.None))!.Metrics.Advance.ShouldBe(2);
        native.RasterizeGlyph(' ', Options(4f, AntiAliasMode.Grayscale))!.Metrics.Advance.ShouldBe(1);
    }

    [Fact]
    public void FixedPitch_SpaceIsTheCell()
    {
        // Source Code's space is 600/1000 em, 7.2 px at 12 px: rounding it up would break the
        // columns every other glyph keeps.
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.SourceCodeVariableOtfBytes());

        int cell = native.RasterizeGlyph(' ', Options(12f, AntiAliasMode.None, HintLevel.None))!.Metrics.Advance;
        native.RasterizeGlyph(' ', Options(12f, AntiAliasMode.None))!.Metrics.Advance.ShouldBe(cell);
        native.RasterizeGlyph(' ', Options(12f, AntiAliasMode.Grayscale))!.Metrics.Advance.ShouldBe(cell);
        native.RasterizeGlyph('n', Options(12f, AntiAliasMode.None))!.Metrics.Advance.ShouldBe(cell);
    }

    [Fact]
    public void Space_ReportsTheRoundingAsItsAdvanceDelta()
    {
        // The fractional metrics' contract: round(advance) + advanceDelta is the written xadvance.
        using var native = Load();
        var space = native.RasterizeGlyph(' ', Options(RobotoCell(12), AntiAliasMode.None) with { FractionalMetrics = true })!;
        var f = space.Fractional.ShouldNotBeNull();
        f.AdvanceDelta.ShouldBe(1f);
        (Math.Round(f.Advance, MidpointRounding.AwayFromZero) + f.AdvanceDelta.Value).ShouldBe(space.Metrics.Advance);

        var unhinted = native.RasterizeGlyph(' ', Options(RobotoCell(12), AntiAliasMode.None, HintLevel.None) with { FractionalMetrics = true })!;
        unhinted.Fractional!.Value.AdvanceDelta.ShouldBeNull();
    }

    [Fact]
    public void Generate_Bilevel_WordGapsReadAsWordGaps()
    {
        // Through BmFont.Generate at a 12 px cell with kerning (the bake on by default): over the
        // pangram, the white between the inks either side of a space is at least two columns and
        // at least twice the median letter gap. Before, "lazy dogs" had one column — a letter gap —
        // because the 'y' overhangs its advance by a column and the space rounded down to two.
        var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), new FontGeneratorOptions
        {
            Size = 12,
            Characters = CharacterSet.FromChars(Pangram),
            AntiAlias = AntiAliasMode.None,
            Kerning = true,
            Rasterizer = new NativeRasterizer(),
        });
        var chars = result.Model.Characters.ToDictionary(c => c.Id);
        var kerning = result.Model.KerningPairs.ToDictionary(k => (k.First, k.Second), k => k.Amount);
        int RightWhite(char c) => chars[c].XAdvance - chars[c].XOffset - chars[c].Width;

        var letterGaps = new List<int>();
        var wordGaps = new List<(string Pair, int Gap)>();
        for (int i = 0; i + 1 < Pangram.Length; i++)
        {
            char a = Pangram[i], b = Pangram[i + 1];
            if (char.IsAsciiLetterLower(a) && char.IsAsciiLetterLower(b))
                letterGaps.Add(RightWhite(a) + chars[b].XOffset + kerning.GetValueOrDefault((a, b)));
            else if (b == ' ' && i + 2 < Pangram.Length)
                wordGaps.Add(($"{a} {Pangram[i + 2]}", RightWhite(a) + chars[' '].XAdvance + chars[Pangram[i + 2]].XOffset));
        }

        letterGaps.Sort();
        int medianLetterGap = letterGaps[letterGaps.Count / 2];
        medianLetterGap.ShouldBe(1, "bilevel letters set one column apart at 9 ppem");
        wordGaps.Count.ShouldBe(9);
        wordGaps.Where(w => w.Gap < Math.Max(2, 2 * medianLetterGap)).ShouldBeEmpty(
            $"word gaps: {string.Join(", ", wordGaps.Select(w => $"'{w.Pair}' {w.Gap}"))}");
    }

    private static NativeRasterizer Load()
    {
        var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        return native;
    }
}
