using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Phase 168 — <see cref="FontGeneratorOptions.WidthPercent"/> and
/// <see cref="FontGeneratorOptions.ItalicAngle"/>: plumbed to backends that report the
/// capability, declined by the rest, and ranged.
/// </summary>
[Collection("RasterizerFactory")]
public class SyntheticWidthAndAngleTests
{
    private static byte[] Roboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static FontGeneratorOptions Options(string chars = "AVTo") => new()
    {
        Size = 32,
        Characters = CharacterSet.FromChars(chars),
        Backend = RasterizerBackend.FreeType,
    };

    [Fact]
    public void RasterOptions_CarryWidthAndAngle()
    {
        var options = Options();
        options.WidthPercent = 80;
        options.ItalicAngle = 20f;

        var raster = RasterOptions.FromGeneratorOptions(options);
        raster.WidthPercent.ShouldBe(80);
        raster.ItalicAngle.ShouldBe(20f);

        var defaults = RasterOptions.FromGeneratorOptions(Options());
        defaults.WidthPercent.ShouldBe(100);
        defaults.ItalicAngle.ShouldBe(12f);
    }

    [Fact]
    public void WidthPercent_OnABackendWithoutTheCapability_IsDeclined()
    {
        var options = Options();
        options.WidthPercent = 80;

        var ex = Should.Throw<NotSupportedException>(() => BmFont.Generate(Roboto(), options));
        ex.Message.ShouldContain("WidthPercent = 80", Case.Sensitive);
    }

    [Fact]
    public void ItalicAngle_OnABackendWithoutTheCapability_IsDeclinedOnlyWhenItWouldSlant()
    {
        var italic = Options();
        italic.Italic = true;
        italic.ItalicAngle = 20f;
        Should.Throw<NotSupportedException>(() => BmFont.Generate(Roboto(), italic))
            .Message.ShouldContain("ItalicAngle = 20", Case.Sensitive);

        // Not italic: the angle slants nothing, so it is not a request the backend declines.
        var upright = Options();
        upright.ItalicAngle = 20f;
        BmFont.Generate(Roboto(), upright).Model.Characters.ShouldNotBeEmpty();

        // The default angle is every backend's own synthetic italic.
        var standard = Options();
        standard.Italic = true;
        BmFont.Generate(Roboto(), standard).Model.Characters.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(9, 12f)]
    [InlineData(1001, 12f)]
    [InlineData(100, 60.5f)]
    [InlineData(100, -61f)]
    [InlineData(100, float.NaN)]
    public void OutOfRangeValues_AreRejected(int widthPercent, float italicAngle)
    {
        var options = Options();
        options.WidthPercent = widthPercent;
        options.ItalicAngle = italicAngle;

        Should.Throw<ArgumentOutOfRangeException>(() => BmFont.Generate(Roboto(), options));
    }

    [Fact]
    public void WidthPercent_ScalesTheFontsKerning()
    {
        static IReadOnlyList<KernSmith.Output.Model.KerningEntry> Kerning(int percent)
        {
            var options = Options("AVTo");
            options.Size = 120;
            options.WidthPercent = percent;
            options.Rasterizer = new WidthScalingBackend();
            return BmFont.Generate(Roboto(), options).Model.KerningPairs;
        }

        var full = Kerning(100).ToDictionary(k => (k.First, k.Second), k => k.Amount);
        var half = Kerning(50).ToDictionary(k => (k.First, k.Second), k => k.Amount);

        full.ShouldNotBeEmpty();
        full[('A', 'V')].ShouldBeLessThan(-1, "the test needs a pair big enough to halve visibly");
        foreach (var (pair, amount) in full)
            Math.Abs(half.GetValueOrDefault(pair) - (amount / 2.0)).ShouldBeLessThanOrEqualTo(0.5, $"{pair}");
    }

    /// <summary>FreeType, reporting <see cref="IRasterizerCapabilities.SupportsWidthScale"/> (kerning comes from core either way).</summary>
    private sealed class WidthScalingBackend : IRasterizer
    {
        private readonly IRasterizer _inner = RasterizerFactory.Create(RasterizerBackend.FreeType);

        public IRasterizerCapabilities Capabilities => new Caps(_inner.Capabilities);

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0) => _inner.LoadFont(fontData, faceIndex);

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options) => _inner.RasterizeGlyph(codepoint, options);

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options) => _inner.RasterizeAll(codepoints, options);

        public RasterizerFontMetrics? GetFontMetrics(RasterOptions options) => _inner.GetFontMetrics(options);

        public void SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes) => _inner.SetVariationAxes(fvarAxes, userAxes);

        public void Dispose() => _inner.Dispose();

        private sealed class Caps(IRasterizerCapabilities inner) : IRasterizerCapabilities
        {
            public bool SupportsColorFonts => inner.SupportsColorFonts;
            public bool SupportsVariableFonts => inner.SupportsVariableFonts;
            public bool SupportsSdf => inner.SupportsSdf;
            public bool SupportsOutlineStroke => false;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes => inner.SupportedAntiAliasModes;
            public bool SupportsWidthScale => true;
        }
    }
}
