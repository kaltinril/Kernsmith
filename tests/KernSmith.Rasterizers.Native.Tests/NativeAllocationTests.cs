using System.Runtime.CompilerServices;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Per-glyph allocation ceilings for the hot paths (Phase 177). A glyph may allocate what it
/// hands back — the bitmap, the outline's command array and a few small result objects — but no
/// scratch: parse, extraction and flattening buffers, the flattened edges included, are pooled,
/// so their size never shows up per glyph.
/// </summary>
public class NativeAllocationTests
{
    /// <summary>Generous room for the small per-glyph objects (RasterizedGlyph, RasterResult,
    /// GlyphOutline, DecodedGlyph and their headers); far below one scratch array.</summary>
    private const long SmallObjectsPerGlyph = 320;

    [Fact]
    public void GetGlyphMetrics_ForSimpleTrueTypeGlyphs_AllocatesNothing()
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = 32 };
        int[] simple = SimpleAsciiGlyphs(native);

        foreach (int codepoint in simple)
            native.GetGlyphMetrics(codepoint, options); // warm caches and JIT

        long allocated = AllocationMeter.BytesAllocatedBy(() =>
        {
            foreach (int codepoint in simple)
                native.GetGlyphMetrics(codepoint, options);
        });

        allocated.ShouldBe(0, $"GetGlyphMetrics allocated {allocated} B over {simple.Length} glyphs");
    }

    [Theory]
    [InlineData(12f)]
    [InlineData(32f)]
    [InlineData(72f)]
    public void RasterizeGlyph_Grayscale_AllocatesOnlyWhatTheGlyphKeeps(float size)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = size };
        int[] simple = SimpleAsciiGlyphs(native);

        // What each glyph legitimately keeps: its bitmap and its outline commands — measured
        // through the same decoder. The flattened edges are pooled, so they are not kept.
        var decoder = new TrueTypeOutlineDecoder(native.Face!);
        long kept = 0;
        foreach (int codepoint in simple)
        {
            var glyph = native.RasterizeGlyph(codepoint, options)!; // also warms caches and JIT
            var outline = decoder.Decode(native.GetGlyphIndex(codepoint), options).Outline;
            kept += ArrayBytes<byte>(glyph.BitmapData.Length)
                + ArrayBytes<OutlineCommand>(outline.Commands.Length);
        }

        long allocated = AllocationMeter.BytesAllocatedBy(() =>
        {
            foreach (int codepoint in simple)
                native.RasterizeGlyph(codepoint, options);
        });

        long scratchPerGlyph = (allocated - kept) / simple.Length;
        scratchPerGlyph.ShouldBeLessThanOrEqualTo(SmallObjectsPerGlyph,
            $"{allocated} B allocated, {kept} B kept by the glyphs: scratch is being allocated per glyph");
    }

    [Fact]
    public void RasterizeGlyph_ScaledColorBitmap_AllocatesNoStrikeSizedScratch()
    {
        // Noto Color Emoji's 136x128 strike scaled to 32px: the decoder's RGBA image (its public
        // result) and the glyph's bitmap are kept; premultiplied and filtered copies of the strike
        // are pooled, as are the decoder's inflate buffers.
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.NotoColorEmojiBytes());
        var options = new RasterOptions { Size = 32, ColorFont = true };
        const int Emoji = 0x1F600;
        var glyph = native.RasterizeGlyph(Emoji, options)!; // also warms caches and JIT
        glyph.Format.ShouldBe(PixelFormat.Rgba32);

        long kept = ArrayBytes<byte>(136 * 128 * 4) + ArrayBytes<byte>(glyph.BitmapData.Length);
        long allocated = AllocationMeter.BytesAllocatedBy(() => native.RasterizeGlyph(Emoji, options));

        // Room for the resampler's tap tables, the inflater's own state and the result objects;
        // a strike-sized float copy alone is 278 KB.
        (allocated - kept).ShouldBeLessThan(16 * 1024, $"{allocated} B allocated, {kept} B kept");
    }

    [Theory]
    [InlineData("cff2")]
    [InlineData("bold")]
    public void GetGlyphMetrics_FullPath_AllocatesOnlyTheOutlinesItBuilds(string path)
    {
        // Paths the box shortcut does not take: a CFF2 charstring at a variable instance, and a
        // simple glyf glyph through synthetic bold. Each builds its outline (and bold a second,
        // emboldened one), but the charstring stacks and the emboldener's per-contour buffers
        // are pooled scratch.
        using var native = new NativeRasterizer();
        var options = new RasterOptions { Size = 32 };
        int[] codepoints;
        if (path == "cff2")
        {
            var bytes = TestFonts.SourceCodeVariableOtfBytes();
            native.LoadFont(bytes);
            var axes = new Dictionary<string, float> { ["wght"] = 900f };
            native.SetVariationAxes(BmFont.ReadFontInfo(bytes).VariationAxes!, axes);
            options = options with { VariationAxes = axes };
            codepoints = Enumerable.Range(0x21, 0x7E - 0x21 + 1).ToArray();
        }
        else
        {
            native.LoadFont(TestFonts.RobotoRegularBytes());
            options = options with { Bold = true };
            codepoints = SimpleAsciiGlyphs(native);
        }

        foreach (int codepoint in codepoints)
            native.GetGlyphMetrics(codepoint, options); // warm caches and JIT

        long allocated = AllocationMeter.BytesAllocatedBy(() =>
        {
            foreach (int codepoint in codepoints)
                native.GetGlyphMetrics(codepoint, options);
        });

        // About 0.8 KB (CFF2) and 1.4 KB (bold) per glyph remain; the scratch was 4-5 KB more.
        long perGlyph = allocated / codepoints.Length;
        perGlyph.ShouldBeLessThan(2048, $"{allocated} B over {codepoints.Length} glyphs");
    }

    [Fact]
    public void GetGlyphMetrics_ColrV1PaintGraph_ReadsTheGraphOnce()
    {
        // Reem Kufi Ink's Arabic-Indic digits are version 1 paint graphs. Measuring one reads its
        // graph once; the version 0 layer shortcut must not read it a second time just to find
        // out that the glyph is not its business.
        using var native = new NativeRasterizer();
        native.LoadFont(TestFonts.ReemKufiInkBytes());
        var options = new RasterOptions { Size = 32, ColorFont = true };
        int[] digits = Enumerable.Range(0x0660, 10).ToArray();

        foreach (int codepoint in digits)
            native.GetGlyphMetrics(codepoint, options); // warm caches and JIT

        long allocated = AllocationMeter.BytesAllocatedBy(() =>
        {
            foreach (int codepoint in digits)
                native.GetGlyphMetrics(codepoint, options);
        });

        // About 1.7 KB per glyph is the graph read and its measurement; a second read adds ~0.4 KB.
        long perGlyph = allocated / digits.Length;
        perGlyph.ShouldBeLessThanOrEqualTo(1700, $"{allocated} B over {digits.Length} glyphs");
    }

    /// <summary>Printable ASCII glyphs Roboto stores as simple (non-composite) outlines.</summary>
    private static int[] SimpleAsciiGlyphs(NativeRasterizer native)
    {
        var face = native.Face!;
        int[] simple = Enumerable.Range(0x21, 0x7E - 0x21 + 1)
            .Where(cp => native.GetGlyphIndex(cp) != 0 && !face.GetGlyph(native.GetGlyphIndex(cp)).IsComposite)
            .ToArray();
        simple.Length.ShouldBeGreaterThan(80, "Roboto's ASCII should be almost entirely simple glyphs");
        return simple;
    }

    /// <summary>The heap size of a one-dimensional array on 64-bit: 24 bytes of header, then the
    /// elements, rounded up to 8.</summary>
    private static long ArrayBytes<T>(int length) => (24 + ((long)length * Unsafe.SizeOf<T>()) + 7) & ~7L;
}
