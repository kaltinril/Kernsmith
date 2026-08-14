using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Phase 183 — tests for the <c>PreparedRasterization</c> prepare/per-add split:
/// the equalize-target override (F3) and the unfiltered-parse option (F7).
/// </summary>
[Collection("RasterizerFactory")]
public class PreparedRasterizationTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static FontGeneratorOptions EqualizeOptions() => new()
    {
        Size = 32,
        Characters = CharacterSet.FromChars("Aax"),
        EqualizeCellHeights = true
    };

    [Fact]
    public void RasterizeAndProcess_EqualizeTargetAboveNaturalMax_PadsAllGlyphsToTarget()
    {
        // Arrange
        var fontData = LoadTestFont();
        using var prepared = PreparedRasterization.Prepare(
            fontData, EqualizeOptions(), systemFontName: null, subsetToCharacterSet: true);
        var codepoints = prepared.ResolveCodepoints();

        var natural = prepared.RasterizeAndProcess(codepoints, equalizeTargetHeight: null);
        var naturalMax = natural.Glyphs.Max(g => g.Height);
        var target = naturalMax + 10;

        // Act
        var padded = prepared.RasterizeAndProcess(codepoints, equalizeTargetHeight: target);

        // Assert — every glyph is padded to exactly the requested cell height
        padded.Glyphs.ShouldAllBe(g => g.Height == target,
            "an equalize target above the natural max should pad every glyph to that target");
    }

    [Fact]
    public void RasterizeAndProcess_EqualizeTargetBelowGlyphHeight_Throws()
    {
        // Arrange
        var fontData = LoadTestFont();
        using var prepared = PreparedRasterization.Prepare(
            fontData, EqualizeOptions(), systemFontName: null, subsetToCharacterSet: true);
        var codepoints = prepared.ResolveCodepoints();

        var natural = prepared.RasterizeAndProcess(codepoints, equalizeTargetHeight: null);
        var naturalMax = natural.Glyphs.Max(g => g.Height);

        // Act + Assert — a glyph taller than the target would change the original atlas
        var ex = Should.Throw<InvalidOperationException>(() =>
            prepared.RasterizeAndProcess(codepoints, equalizeTargetHeight: naturalMax - 1));
        ex.Message.ShouldContain("equalized cell height", Case.Sensitive);
    }

    [Fact]
    public void RasterizeAndProcess_NullEqualizeTarget_MatchesRasterizeFont()
    {
        // Arrange
        var fontData = LoadTestFont();
        var viaWrapper = BmFont.RasterizeFont(fontData, EqualizeOptions());

        using var prepared = PreparedRasterization.Prepare(
            fontData, EqualizeOptions(), systemFontName: null, subsetToCharacterSet: true);

        // Act
        var viaSplit = prepared.RasterizeAndProcess(prepared.ResolveCodepoints(), equalizeTargetHeight: null);

        // Assert — same glyphs, same max-height equalization, same pixels
        viaSplit.Glyphs.Count.ShouldBe(viaWrapper.Glyphs.Count);
        var wrapperGlyphs = viaWrapper.Glyphs.OrderBy(g => g.Codepoint).ToList();
        var splitGlyphs = viaSplit.Glyphs.OrderBy(g => g.Codepoint).ToList();
        for (var i = 0; i < wrapperGlyphs.Count; i++)
        {
            splitGlyphs[i].Codepoint.ShouldBe(wrapperGlyphs[i].Codepoint);
            splitGlyphs[i].Width.ShouldBe(wrapperGlyphs[i].Width);
            splitGlyphs[i].Height.ShouldBe(wrapperGlyphs[i].Height);
            splitGlyphs[i].BitmapData.ShouldBe(wrapperGlyphs[i].BitmapData);
        }
    }

    [Fact]
    public void Prepare_WithoutSubsetting_ParsesFullCmapAndKerning()
    {
        // Arrange — character set contains only 'A'
        var fontData = LoadTestFont();
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("A")
        };

        // Act
        using var subsetted = PreparedRasterization.Prepare(
            fontData, options, systemFontName: null, subsetToCharacterSet: true);
        using var full = PreparedRasterization.Prepare(
            fontData, options, systemFontName: null, subsetToCharacterSet: false);

        // Assert — the unfiltered parse resolves codepoints outside the character set (F7)
        full.FontInfo.AvailableCodepoints.ShouldContain((int)'Q',
            "an unfiltered parse must expose the full cmap so later-added characters resolve");
        subsetted.FontInfo.AvailableCodepoints.ShouldNotContain((int)'Q',
            "the subsetted parse filters the cmap to the requested character set");

        // Kerning pairs for characters outside the set survive the unfiltered parse only.
        full.FontInfo.KerningPairs.ShouldContain(
            p => p.LeftCodepoint != 'A' && p.RightCodepoint != 'A',
            "an unfiltered parse must keep kerning pairs for characters outside the initial set");
        subsetted.FontInfo.KerningPairs.ShouldNotContain(
            p => p.LeftCodepoint != 'A' && p.RightCodepoint != 'A',
            "the subsetted parse drops kerning pairs for characters outside the set");
    }

    [Fact]
    public void RasterizeAndProcess_RepeatedCalls_QueryFontMetricsAndKerningOnce()
    {
        // GetFontMetrics/GetKerningPairs depend only on the fixed raster options, so a
        // session's repeated per-add calls must not re-query the rasterizer every time.
        var fontData = LoadTestFont();
        var counting = new CountingRasterizer();
        var options = new FontGeneratorOptions { Size = 32, Rasterizer = counting };
        using var prepared = PreparedRasterization.Prepare(
            fontData, options, systemFontName: null, subsetToCharacterSet: false);

        prepared.RasterizeAndProcess(new[] { (int)'A' }, equalizeTargetHeight: null);
        prepared.RasterizeAndProcess(new[] { (int)'B' }, equalizeTargetHeight: null);

        counting.FontMetricsCalls.ShouldBe(1);
        counting.KerningPairsCalls.ShouldBe(1);
    }

    /// <summary>Counts metric/kerning queries; rasterizes nothing.</summary>
    private sealed class CountingRasterizer : KernSmith.Rasterizer.IRasterizer
    {
        public int FontMetricsCalls { get; private set; }
        public int KerningPairsCalls { get; private set; }

        public KernSmith.Rasterizer.IRasterizerCapabilities Capabilities { get; } = new Caps();

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
        {
        }

        public KernSmith.Rasterizer.RasterizedGlyph? RasterizeGlyph(int codepoint, KernSmith.Rasterizer.RasterOptions options)
            => null;

        public IReadOnlyList<KernSmith.Rasterizer.RasterizedGlyph> RasterizeAll(
            IEnumerable<int> codepoints, KernSmith.Rasterizer.RasterOptions options)
            => Array.Empty<KernSmith.Rasterizer.RasterizedGlyph>();

        public KernSmith.Rasterizer.RasterizerFontMetrics? GetFontMetrics(KernSmith.Rasterizer.RasterOptions options)
        {
            FontMetricsCalls++;
            return new KernSmith.Rasterizer.RasterizerFontMetrics { Ascent = 20, Descent = 6, LineHeight = 26 };
        }

        public IReadOnlyList<KernSmith.Font.Models.ScaledKerningPair>? GetKerningPairs(KernSmith.Rasterizer.RasterOptions options)
        {
            KerningPairsCalls++;
            return Array.Empty<KernSmith.Font.Models.ScaledKerningPair>();
        }

        public void Dispose()
        {
        }

        private sealed class Caps : KernSmith.Rasterizer.IRasterizerCapabilities
        {
            public bool SupportsColorFonts => false;
            public bool SupportsVariableFonts => false;
            public bool SupportsSdf => false;
            public bool SupportsOutlineStroke => false;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } = new[] { AntiAliasMode.Grayscale };
        }
    }
}
