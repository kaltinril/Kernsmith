using KernSmith.Font;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// <see cref="FontGeneratorOptions.FractionalMetrics"/>: the unrounded advance and left side
/// bearing of every glyph, and what hinting moved them by, written to the extended metadata
/// (<see cref="ExtendedMetadata.FractionalMetrics"/>; text <c>kernsmithchar</c> lines, XML
/// <c>&lt;char&gt;</c> children of <c>&lt;kernsmith&gt;</c>, binary block 6 <c>chars</c>) in
/// 1/64 px steps, for renderers that position glyphs fractionally. Off by default, so default
/// output is byte-identical.
/// </summary>
[Collection("RasterizerFactory")]
public sealed class FractionalMetricsTests
{
    private static readonly string FontPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static byte[] Font() => File.ReadAllBytes(FontPath);

    private static FontGeneratorOptions Options(bool fractional = true, Action<FontGeneratorOptions>? configure = null)
    {
        var options = new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.Ascii,
            Kerning = false,
            FractionalMetrics = fractional,
        };
        configure?.Invoke(options);
        return options;
    }

    private static FontInfo Info() => new TtfFontReader().ReadFont(Font());

    // ---- gating -----------------------------------------------------------------------------------

    [Fact]
    public void Default_IsOff_AndWritesNothing()
    {
        new FontGeneratorOptions().FractionalMetrics.ShouldBeFalse();
        RasterOptions.FromGeneratorOptions(new FontGeneratorOptions()).FractionalMetrics.ShouldBeFalse();

        var result = BmFont.Generate(Font(), Options(fractional: false));

        result.Model.Extended.ShouldBeNull();
        result.FntText.ShouldNotContain("kernsmithchar", Case.Sensitive);
        result.FntText.ShouldNotContain(" lsb=", Case.Sensitive);
    }

    [Fact]
    public void On_WritesOneEntryPerCharacter_InCharacterOrder()
    {
        var result = BmFont.Generate(Font(), Options());

        var table = result.Model.Extended.ShouldNotBeNull().FractionalMetrics.ShouldNotBeNull();
        table.Select(f => f.Id).ShouldBe(result.Model.Characters.Select(c => c.Id));
        result.FntText.ShouldContain("kernsmithchar id=65 advance=", Case.Sensitive);
        result.FntText.Split('\n').Count(l => l.StartsWith("kernsmithchar ", StringComparison.Ordinal)).ShouldBe(result.Model.Characters.Count);
    }

    // ---- values -----------------------------------------------------------------------------------

    [Fact]
    public void Values_AreInSixtyFourths_AndNotJustTheRoundedIntegers()
    {
        var result = BmFont.Generate(Font(), Options(configure: o => o.EnableHinting = false));

        var table = result.Model.Extended!.FractionalMetrics!;
        foreach (var f in table)
        {
            (f.Advance * 64).ShouldBe(MathF.Round(f.Advance * 64), 1e-3, $"advance of U+{f.Id:X4} is not a multiple of 1/64");
            (f.Lsb * 64).ShouldBe(MathF.Round(f.Lsb * 64), 1e-3, $"lsb of U+{f.Id:X4} is not a multiple of 1/64");
            f.AdvanceDelta.ShouldBeNull("unhinted: no delta");
            f.LsbDelta.ShouldBeNull("unhinted: no delta");
        }

        table.Count(f => f.Advance != MathF.Round(f.Advance)).ShouldBeGreaterThan(table.Count / 2, "most advances are fractional at this size");

        // Unhinted, the written xadvance is the fractional advance rounded.
        var chars = result.Model.Characters.ToDictionary(c => c.Id);
        foreach (var f in table)
            Math.Abs(f.Advance - chars[f.Id].XAdvance).ShouldBeLessThanOrEqualTo(0.5f + (1f / 64), $"U+{f.Id:X4}");
    }

    [Fact]
    public void Advances_ScaleLinearlyWithTheFontsDesignAdvances()
    {
        // No hinting, no rounding: every glyph's fractional advance is its hmtx advance times one
        // scale, so the ratio of any two is the ratio of their design advances.
        var result = BmFont.Generate(Font(), Options(configure: o => o.EnableHinting = false));
        var design = Info().DesignMetrics;
        var table = result.Model.Extended!.FractionalMetrics!.ToDictionary(f => f.Id);

        float scale = table['M'].Advance / design['M'].AdvanceWidth;
        foreach (var (id, f) in table)
        {
            if (design[id].AdvanceWidth == 0)
                continue;
            f.Advance.ShouldBe(design[id].AdvanceWidth * scale, 1f / 64 + 1e-3, $"U+{id:X4}");
            f.Lsb.ShouldBe(design[id].LeftSideBearing * scale, 1f / 64 + 1e-3, $"lsb of U+{id:X4}");
        }
    }

    [Fact]
    public void FreeType_Hinted_ReportsTheLinearAdvance_AndTheDeltaThatClosesToXAdvance()
    {
        // FreeType's hinted advance is a whole number of pixels; the linear advance plus what
        // hinting added is exactly the xadvance written.
        var result = BmFont.Generate(Font(), Options(configure: o =>
        {
            o.Size = 12;
            o.Rasterizer = new FreeTypeRasterizer();
            o.HintLevel = HintLevel.Full;
        }));
        var chars = result.Model.Characters.ToDictionary(c => c.Id);

        var table = result.Model.Extended!.FractionalMetrics!;
        table.Count(f => f.AdvanceDelta.HasValue).ShouldBe(table.Count, "every glyph was hinted");
        table.Count(f => f.AdvanceDelta != 0).ShouldBeGreaterThan(0, "hinting rounds advances");
        foreach (var f in table)
            (f.Advance + f.AdvanceDelta!.Value).ShouldBe(chars[f.Id].XAdvance, 1e-3, $"U+{f.Id:X4}");
    }

    [Fact]
    public void FreeTypeRasterizer_ReportsTheFontsAdvanceAtTheRequestedPpem()
    {
        using var freeType = new FreeTypeRasterizer();
        freeType.LoadFont(Font());
        var design = Info().DesignMetrics;
        int upem = Info().UnitsPerEm;

        var options = new RasterOptions { Size = 64, Dpi = 72, FractionalMetrics = true, EnableHinting = false };
        foreach (char c in "AMiWg.")
        {
            var glyph = freeType.RasterizeGlyph(c, options).ShouldNotBeNull();
            var f = glyph.Fractional.ShouldNotBeNull();
            f.Advance.ShouldBe(design[c].AdvanceWidth * 64f / upem, 1f / 64 + 1e-3, c.ToString());
            f.BearingX.ShouldBe(design[c].LeftSideBearing * 64f / upem, 1f / 64 + 1e-3, c.ToString());
            f.AdvanceDelta.ShouldBeNull();
        }

        freeType.RasterizeGlyph('A', options with { FractionalMetrics = false })!.Fractional.ShouldBeNull();
    }

    [Fact]
    public void SuperSampling_DoesNotChangeTheFractionalAdvance()
    {
        var plain = BmFont.Generate(Font(), Options(configure: o => o.EnableHinting = false)).Model.Extended!.FractionalMetrics!.ToDictionary(f => f.Id);
        var sampled = BmFont.Generate(Font(), Options(configure: o => { o.EnableHinting = false; o.SuperSampleLevel = 4; })).Model.Extended!.FractionalMetrics!;

        foreach (var f in sampled)
        {
            f.Advance.ShouldBe(plain[f.Id].Advance, 1f / 64 + 1e-3, $"U+{f.Id:X4}");
            f.Lsb.ShouldBe(plain[f.Id].Lsb, 1f / 64 + 1e-3, $"lsb of U+{f.Id:X4}");
        }
    }

    [Fact]
    public void AdvanceAdjustX_AndEffects_MoveTheFractionalAdvance_AsTheyMoveXAdvance()
    {
        var plain = BmFont.Generate(Font(), Options(configure: o => o.EnableHinting = false));
        var adjusted = BmFont.Generate(Font(), Options(configure: o => { o.EnableHinting = false; o.AdvanceAdjustX = 1.5f; }));

        var before = plain.Model.Extended!.FractionalMetrics!.ToDictionary(f => f.Id);
        foreach (var f in adjusted.Model.Extended!.FractionalMetrics!)
            f.Advance.ShouldBe(before[f.Id].Advance + 1.5f, 1e-3, $"U+{f.Id:X4}");
    }

    [Fact]
    public void BackendWithoutFractionalMetrics_GetsTheDesignAdvance_FromTheSharedParser()
    {
        // A backend that reports nothing still gets the font's own linear advance and bearing at
        // the render size (and no hinting deltas, since nothing is known about its hinting).
        var result = BmFont.Generate(Font(), Options(configure: o => o.Rasterizer = new BlockRasterizer()));
        var design = Info().DesignMetrics;

        var table = result.Model.Extended!.FractionalMetrics!.ToDictionary(f => f.Id);
        table.Count.ShouldBe(result.Model.Characters.Count);
        float scale = table['M'].Advance / design['M'].AdvanceWidth;
        table['i'].Advance.ShouldBe(design['i'].AdvanceWidth * scale, 1f / 64 + 1e-3);
        table['i'].AdvanceDelta.ShouldBeNull();
        table['i'].LsbDelta.ShouldBeNull();
    }

    // ---- round trips ----------------------------------------------------------------------------------

    [Fact]
    public void RoundTrip_Text_Xml_Binary_KeepEveryValue()
    {
        var original = BmFont.Generate(Font(), Options(configure: o => { o.Size = 12; o.HintLevel = HintLevel.Full; }));
        var expected = original.Model.Extended!.FractionalMetrics!;
        expected.ShouldContain(f => f.AdvanceDelta.HasValue);

        BmFontReader.ReadText(original.ToString()).Extended!.FractionalMetrics.ShouldBe(expected);
        BmFontReader.ReadXml(original.ToXml()).Extended!.FractionalMetrics.ShouldBe(expected);
        BmFontReader.ReadBinary(original.ToBinary()).Extended!.FractionalMetrics.ShouldBe(expected);
    }

    [Fact]
    public void Reader_TakesKernsmithcharLines_WithoutAKernsmithLine()
    {
        const string fnt =
            "info face=\"Test\" size=32 bold=0 italic=0 charset=\"\" unicode=1 stretchH=100 smooth=1 aa=1 padding=0,0,0,0 spacing=0,0\n" +
            "common lineHeight=38 base=30 scaleW=256 scaleH=256 pages=1 packed=0\n" +
            "page id=0 file=\"test_0.png\"\n" +
            "chars count=1\n" +
            "char id=65 x=0 y=0 width=10 height=10 xoffset=1 yoffset=2 xadvance=12 page=0 chnl=15\n" +
            "kernsmithchar id=65 advance=11.578125 lsb=0.984375 advanceDelta=0.421875 lsbDelta=-0.25\n";

        var model = BmFontReader.ReadText(fnt);

        var entry = model.Extended.ShouldNotBeNull().FractionalMetrics.ShouldNotBeNull().ShouldHaveSingleItem();
        entry.ShouldBe(new FractionalCharMetrics(65, 11.578125f, 0.984375f, 0.421875f, -0.25f));
    }

    [Fact]
    public void BmfcRoundTrip_WritesTheKeyOnlyWhenOn()
    {
        string on = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions { FractionalMetrics = true }));
        on.ShouldContain("fractionalMetrics=1", Case.Sensitive);
        BmfcConfigReader.Parse(on).Options.FractionalMetrics.ShouldBeTrue();

        string off = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions()));
        off.ShouldNotContain("fractionalMetrics", Case.Sensitive);
        BmfcConfigReader.Parse(off).Options.FractionalMetrics.ShouldBeFalse();
    }

    [Fact]
    public void Builder_SetsAndCopiesTheOption()
    {
        var built = BmFont.Builder().WithFont(Font()).WithSize(24).WithCharacters(CharacterSet.FromChars("AB")).WithFractionalMetrics().Build();
        built.Model.Extended.ShouldNotBeNull().FractionalMetrics.ShouldNotBeNull().Count.ShouldBe(2);

        var config = new BmfcConfig { Options = new FontGeneratorOptions { Size = 24, Characters = CharacterSet.FromChars("AB"), FractionalMetrics = true } };
        var fromConfig = BmFont.Builder().FromConfig(config).WithFont(Font()).Build();
        fromConfig.Model.Extended.ShouldNotBeNull().FractionalMetrics.ShouldNotBeNull().Count.ShouldBe(2);
    }

    /// <summary>A backend that draws every glyph as an 8x8 block and reports no fractional metrics.</summary>
    private sealed class BlockRasterizer : IRasterizer
    {
        public IRasterizerCapabilities Capabilities { get; } = new Caps();

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
        {
        }

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
        {
            var pixels = new byte[8 * 8];
            Array.Fill(pixels, (byte)255);
            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = codepoint,
                BitmapData = pixels,
                Width = 8,
                Height = 8,
                Pitch = 8,
                Metrics = new GlyphMetrics(0, 8, 10, 8, 8),
                Format = PixelFormat.Grayscale8,
            };
        }

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options) =>
            codepoints.Select(c => RasterizeGlyph(c, options)!).ToList();

        public void Dispose()
        {
        }

        private sealed class Caps : IRasterizerCapabilities
        {
            public bool SupportsColorFonts => false;
            public bool SupportsVariableFonts => false;
            public bool SupportsSdf => false;
            public bool SupportsOutlineStroke => false;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } = [AntiAliasMode.Grayscale];
        }
    }
}
