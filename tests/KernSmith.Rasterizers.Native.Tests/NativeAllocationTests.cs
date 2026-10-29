using System.Runtime.CompilerServices;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Per-glyph allocation ceilings for the hot paths (Phase 177). A glyph may allocate what it
/// hands back — the bitmap, the outline's command array, the edge array the fill consumes and a
/// few small result objects — but no scratch: parse, extraction and flattening buffers are
/// reused, so their size never shows up per glyph.
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

        // What each glyph legitimately keeps: its bitmap, its outline commands and the exact
        // edge array the fill reads — measured through the same decoder and flattener.
        var decoder = new TrueTypeOutlineDecoder(native.Face!);
        var toPixels = new OutlineTransform(GlyphPipeline.PixelScale(options, native.Face!.Head.UnitsPerEm), 0f);
        long kept = 0;
        foreach (int codepoint in simple)
        {
            var glyph = native.RasterizeGlyph(codepoint, options)!; // also warms caches and JIT
            var outline = decoder.Decode(native.GetGlyphIndex(codepoint), options).Outline;
            kept += ArrayBytes<byte>(glyph.BitmapData.Length)
                + ArrayBytes<OutlineCommand>(outline.Commands.Length)
                + ArrayBytes<EdgeSegment>(OutlineFlattener.Flatten(outline, toPixels).Length);
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
