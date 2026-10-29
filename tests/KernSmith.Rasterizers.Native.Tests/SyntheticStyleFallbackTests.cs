using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 150 Issue 5 — the core pipeline skips a caller-supplied
/// <see cref="BoldPostProcessor"/>/<see cref="ItalicPostProcessor"/> only when the backend
/// reports <see cref="IRasterizerCapabilities.SupportsSyntheticBold"/>/<see cref="IRasterizerCapabilities.SupportsSyntheticItalic"/>,
/// because only then has the backend already applied the style itself. A backend without the
/// capability ignores the flag, so dropping the fallback there produced unstyled (narrower)
/// glyphs with no error.
/// </summary>
public class SyntheticStyleFallbackTests
{
    private const string SingleChar = "A";

    /// <summary>Expand by 2px a side, so the fallback is unmistakable in the cell size.</summary>
    private const int Strength = 2;

    [Fact]
    public void Generate_BoldRequestedOnBackendWithoutSyntheticBold_StillRunsTheSuppliedFallback()
    {
        var withoutBoldFlag = GlyphWidth(bold: false, italic: false, Unstyled);
        var withBoldFlag = GlyphWidth(bold: true, italic: false, Unstyled);

        // Both runs supply the same BoldPostProcessor. The backend cannot embolden outlines
        // itself, so asking for bold must not cause the core to discard the fallback.
        withBoldFlag.ShouldBe(withoutBoldFlag,
            "requesting bold on a backend that cannot embolden must not drop the caller's "
            + "BoldPostProcessor — it produced a narrower glyph than not requesting bold at all");
    }

    [Fact]
    public void Generate_ItalicRequestedOnBackendWithoutSyntheticItalic_StillRunsTheSuppliedFallback()
    {
        var withoutItalicFlag = GlyphWidth(bold: false, italic: false, Unstyled, useItalicProcessor: true);
        var withItalicFlag = GlyphWidth(bold: false, italic: true, Unstyled, useItalicProcessor: true);

        withItalicFlag.ShouldBe(withoutItalicFlag,
            "requesting italic on a backend that cannot slant must not drop the caller's "
            + "ItalicPostProcessor");
    }

    [Fact]
    public void Generate_BoldRequestedOnBackendWithSyntheticBold_StillSkipsTheFallback()
    {
        // The other half of the contract: FreeType does embolden outlines, so the core must
        // keep skipping the post-processor there or the glyph gets emboldened twice.
        var withoutBoldFlag = GlyphWidth(bold: false, italic: false, Backend(RasterizerBackend.FreeType));
        var withBoldFlag = GlyphWidth(bold: true, italic: false, Backend(RasterizerBackend.FreeType));

        withBoldFlag.ShouldBeLessThan(withoutBoldFlag,
            "FreeType applies bold at the outline level, so the dilation fallback must stay "
            + "skipped rather than stacking on top of it");
    }

    [Fact]
    public void Generate_BoldRequestedOnNative_SkipsTheFallback()
    {
        // Native emboldens outlines itself (Phase 167), so its bold must replace the 2px-a-side
        // dilation fallback, not stack on top of it.
        var withoutBoldFlag = GlyphWidth(bold: false, italic: false, Backend(RasterizerBackend.Native));
        var withBoldFlag = GlyphWidth(bold: true, italic: false, Backend(RasterizerBackend.Native));

        withBoldFlag.ShouldBeLessThan(withoutBoldFlag,
            "Native applies bold at the outline level, so the dilation fallback must be skipped");
    }

    [Fact]
    public void Generate_ItalicRequestedOnNative_SkipsTheFallback()
    {
        // With the fallback skipped, the only slant is Native's own — the same glyph a run
        // without the post-processor produces.
        var withFallback = GlyphWidth(bold: false, italic: true, Backend(RasterizerBackend.Native), useItalicProcessor: true);
        var nativeOnly = GlyphWidth(bold: false, italic: true, Backend(RasterizerBackend.Native), useItalicProcessor: true, supplyProcessor: false);

        withFallback.ShouldBe(nativeOnly);
    }

    [Fact]
    public void Generate_NativeBold_IsWiderThanRegular()
    {
        // The regression harness cannot tell "styled" from "silently unstyled", so pin that
        // Native's bold actually changes the output.
        var regular = GlyphWidth(bold: false, italic: false, Backend(RasterizerBackend.Native), supplyProcessor: false);
        var bold = GlyphWidth(bold: true, italic: false, Backend(RasterizerBackend.Native), supplyProcessor: false);

        bold.ShouldBeGreaterThan(regular);
    }

    private static readonly Action<FontGeneratorOptions> Unstyled = o => o.Rasterizer = new StyleIgnoringRasterizer();

    private static Action<FontGeneratorOptions> Backend(RasterizerBackend backend) => o => o.Backend = backend;

    private static int GlyphWidth(
        bool bold,
        bool italic,
        Action<FontGeneratorOptions> selectRasterizer,
        bool useItalicProcessor = false,
        bool supplyProcessor = true)
    {
        IGlyphPostProcessor processor = useItalicProcessor
            ? new ItalicPostProcessor()
            : new BoldPostProcessor(Strength);

        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(SingleChar),
            Bold = bold,
            Italic = italic,
            PostProcessors = supplyProcessor ? [processor] : null
        };
        selectRasterizer(options);

        try
        {
            var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), options);
            return BmFontReader.ReadText(result.FntText).Characters.First(c => c.Id == 'A').Width;
        }
        finally
        {
            options.Rasterizer?.Dispose();
        }
    }

    /// <summary>
    /// A backend without synthetic bold/italic: Native with the style flags stripped, reporting
    /// both capabilities false.
    /// </summary>
    private sealed class StyleIgnoringRasterizer : IRasterizer
    {
        private readonly NativeRasterizer _inner = new();

        public IRasterizerCapabilities Capabilities { get; } = new Caps();

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0) => _inner.LoadFont(fontData, faceIndex);

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options) =>
            _inner.RasterizeGlyph(codepoint, Strip(options));

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options) =>
            _inner.RasterizeAll(codepoints, Strip(options));

        public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options) =>
            _inner.GetGlyphMetrics(codepoint, Strip(options));

        public RasterizerFontMetrics? GetFontMetrics(RasterOptions options) => _inner.GetFontMetrics(Strip(options));

        public void Dispose() => _inner.Dispose();

        private static RasterOptions Strip(RasterOptions options) => options with { Bold = false, Italic = false };

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
