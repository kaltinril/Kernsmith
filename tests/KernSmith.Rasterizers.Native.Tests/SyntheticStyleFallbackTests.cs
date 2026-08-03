using KernSmith.Output;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 150 Issue 5 — the core pipeline skips a caller-supplied
/// <see cref="BoldPostProcessor"/>/<see cref="ItalicPostProcessor"/> whenever
/// <c>Bold</c>/<c>Italic</c> is set, on the assumption that the backend already applied the
/// transform itself. Native is the first backend to report
/// <see cref="IRasterizerCapabilities.SupportsSyntheticBold"/> false, so for it that
/// assumption is wrong and asking for bold used to make glyphs *thinner* than not asking.
/// </summary>
public class SyntheticStyleFallbackTests
{
    private const string SingleChar = "A";

    /// <summary>Expand by 2px a side, so the fallback is unmistakable in the cell size.</summary>
    private const int Strength = 2;

    [Fact]
    public void Generate_BoldRequestedOnBackendWithoutSyntheticBold_StillRunsTheSuppliedFallback()
    {
        var withoutBoldFlag = GlyphWidth(bold: false, italic: false);
        var withBoldFlag = GlyphWidth(bold: true, italic: false);

        // Both runs supply the same BoldPostProcessor. Native cannot embolden outlines itself,
        // so asking for bold must not cause the core to discard the fallback.
        withBoldFlag.ShouldBe(withoutBoldFlag,
            "requesting bold on a backend that cannot embolden must not drop the caller's "
            + "BoldPostProcessor — it produced a narrower glyph than not requesting bold at all");
    }

    [Fact]
    public void Generate_ItalicRequestedOnBackendWithoutSyntheticItalic_StillRunsTheSuppliedFallback()
    {
        var withoutItalicFlag = GlyphWidth(bold: false, italic: false, useItalicProcessor: true);
        var withItalicFlag = GlyphWidth(bold: false, italic: true, useItalicProcessor: true);

        withItalicFlag.ShouldBe(withoutItalicFlag,
            "requesting italic on a backend that cannot slant must not drop the caller's "
            + "ItalicPostProcessor");
    }

    [Fact]
    public void Generate_BoldRequestedOnBackendWithSyntheticBold_StillSkipsTheFallback()
    {
        // The other half of the contract: FreeType does embolden outlines, so the core must
        // keep skipping the post-processor there or the glyph gets emboldened twice.
        var withoutBoldFlag = GlyphWidth(bold: false, italic: false, backend: RasterizerBackend.FreeType);
        var withBoldFlag = GlyphWidth(bold: true, italic: false, backend: RasterizerBackend.FreeType);

        withBoldFlag.ShouldBeLessThan(withoutBoldFlag,
            "FreeType applies bold at the outline level, so the dilation fallback must stay "
            + "skipped rather than stacking on top of it");
    }

    private static int GlyphWidth(
        bool bold,
        bool italic,
        bool useItalicProcessor = false,
        RasterizerBackend backend = RasterizerBackend.Native)
    {
        IGlyphPostProcessor processor = useItalicProcessor
            ? new ItalicPostProcessor()
            : new BoldPostProcessor(Strength);

        var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(SingleChar),
            Backend = backend,
            Bold = bold,
            Italic = italic,
            PostProcessors = [processor]
        });

        return BmFontReader.ReadText(result.FntText).Characters.First(c => c.Id == 'A').Width;
    }
}
