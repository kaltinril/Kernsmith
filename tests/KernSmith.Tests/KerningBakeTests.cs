using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// <see cref="FontGeneratorOptions.BakeHintingIntoKerning"/>: a backend's per-pair spacing
/// corrections (<see cref="IRasterizer.GetKerningAdjustments"/>) are folded into the kerning
/// pairs the model writes — added to the font's own pair before the one rounding, a pair of
/// their own where the font has none, dropped when the sum rounds to nothing.
/// </summary>
[Collection("RasterizerFactory")]
public sealed class KerningBakeTests
{
    private static readonly string FontPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static byte[] Font() => File.ReadAllBytes(FontPath);

    private static FontGeneratorOptions Options(IRasterizer rasterizer, bool? bake) => new()
    {
        Size = 24,
        Characters = CharacterSet.FromChars("FAno"),
        Kerning = true,
        BakeHintingIntoKerning = bake,
        Rasterizer = rasterizer,
    };

    [Fact]
    public void Default_LeavesItToTheBackend_ExplicitFalseDoesNotAsk()
    {
        // Unset, the backend is asked and decides (every shipped backend but Native answers
        // null); false never asks.
        new FontGeneratorOptions().BakeHintingIntoKerning.ShouldBeNull();

        var asked = new AdjustingRasterizer([new ScaledKerningAdjustment('n', 'o', 0.8f)]);
        var result = BmFont.Generate(Font(), Options(asked, bake: null));
        asked.AdjustmentsRequested.ShouldBe(1);
        result.Model.KerningPairs.ShouldContain(p => p.First == 'n' && p.Second == 'o' && p.Amount == 1);

        var off = new AdjustingRasterizer([new ScaledKerningAdjustment('n', 'o', 0.8f)]);
        result = BmFont.Generate(Font(), Options(off, bake: false));
        off.AdjustmentsRequested.ShouldBe(0);
        result.Model.KerningPairs.ShouldNotContain(p => p.First == 'n' && p.Second == 'o');
    }

    [Fact]
    public void Adjustments_AreAddedToTheFontsPairs_BeforeRounding()
    {
        // Roboto kerns F/A by -170 units: -1.51 px at this size, written as -2. The backend's
        // correction is added to that unrounded amount and the sum rounded once: +1.2 makes it
        // -0.31, nothing, where rounding the two apart (-2 + 1) would leave -1; -0.6 makes it
        // -2.11, still -2, where -2 + -1 would be -3.
        static int? Fa(float correction) =>
            BmFont.Generate(Font(), Options(new AdjustingRasterizer([new ScaledKerningAdjustment('F', 'A', correction)]), bake: true))
                .Model.KerningPairs.SingleOrDefault(p => p.First == 'F' && p.Second == 'A')?.Amount;

        BmFont.Generate(Font(), Options(new AdjustingRasterizer([]), bake: true))
            .Model.KerningPairs.Single(p => p.First == 'F' && p.Second == 'A').Amount.ShouldBe(-2);
        Fa(1.2f).ShouldBeNull();
        Fa(-0.6f).ShouldBe(-2);
        Fa(0.6f).ShouldBe(-1);
    }

    [Fact]
    public void Adjustments_MakeNewPairs_AndDropWhatRoundsToNothing()
    {
        var rasterizer = new AdjustingRasterizer(
        [
            new ScaledKerningAdjustment('n', 'o', 0.8f),
            new ScaledKerningAdjustment('o', 'n', -0.6f),
            new ScaledKerningAdjustment('n', 'n', 0.3f),
            new ScaledKerningAdjustment('n', 'Z', 2f),
        ]);

        var result = BmFont.Generate(Font(), Options(rasterizer, bake: true));

        rasterizer.AdjustmentsRequested.ShouldBe(1);
        var pairs = result.Model.KerningPairs.ToDictionary(p => (p.First, p.Second), p => p.Amount);
        pairs[('n', 'o')].ShouldBe(1);
        pairs[('o', 'n')].ShouldBe(-1);
        pairs.ShouldNotContainKey(('n', 'n'));
        pairs.ShouldNotContainKey(('n', 'Z'));
    }

    [Fact]
    public void Adjustments_ReachTheTextOutput()
    {
        var result = BmFont.Generate(Font(), Options(new AdjustingRasterizer([new ScaledKerningAdjustment('n', 'o', 0.8f)]), bake: true));

        result.FntText.ShouldContain("kerning first=110 second=111 amount=1");
    }

    [Theory]
    [InlineData(true, "bakeHintingIntoKerning=1")]
    [InlineData(false, "bakeHintingIntoKerning=0")]
    public void BmfcRoundTrip_KeepsAnExplicitSetting(bool bake, string line)
    {
        var options = new FontGeneratorOptions { BakeHintingIntoKerning = bake };

        string text = BmfcConfigWriter.Write(BmfcConfig.FromOptions(options));
        var read = BmfcConfigReader.Parse(text);

        text.ShouldContain(line, Case.Sensitive);
        read.Options.BakeHintingIntoKerning.ShouldBe(bake);
    }

    [Fact]
    public void Builder_FromConfig_KeepsTheBakeSettingAndGamma()
    {
        var config = BmfcConfigReader.Parse($"fontFile={FontPath}\nfontSize=24\nchars=65\nbakeHintingIntoKerning=0\ngamma=2.2\n");
        var rasterizer = new AdjustingRasterizer([]);

        BmFont.Builder().FromConfig(config).WithRasterizer(rasterizer).Build();

        rasterizer.LastOptions.ShouldNotBeNull();
        rasterizer.LastOptions.BakeHintingIntoKerning.ShouldBe(false);
        rasterizer.LastOptions.Gamma.ShouldBe(2.2f);
    }

    [Fact]
    public void BmfcRoundTrip_WritesNothing_WhenUnset()
    {
        string text = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions()));

        text.ShouldNotContain("bakeHintingIntoKerning", Case.Sensitive);
        BmfcConfigReader.Parse(text).Options.BakeHintingIntoKerning.ShouldBeNull();
    }

    /// <summary>A grayscale backend that draws every glyph as an 8x8 block and reports the adjustments it is given.</summary>
    private sealed class AdjustingRasterizer(IReadOnlyList<ScaledKerningAdjustment> adjustments) : IRasterizer
    {
        public int AdjustmentsRequested { get; private set; }

        /// <summary>The options of the last glyph rasterized.</summary>
        public RasterOptions? LastOptions { get; private set; }

        public IRasterizerCapabilities Capabilities { get; } = new Caps();

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
        {
        }

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
        {
            LastOptions = options;
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

        public IReadOnlyList<ScaledKerningAdjustment>? GetKerningAdjustments(RasterOptions options)
        {
            AdjustmentsRequested++;
            return adjustments;
        }

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
