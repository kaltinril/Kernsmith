using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 166 Part 1 — the staged glyph pipeline's seams: decode, transform list, bounds,
/// scale/hint and render-mode dispatch.
/// </summary>
public class GlyphPipelineTests
{
    private static readonly RasterOptions Size32 = new() { Size = 32 };

    private static NativeFontFace Roboto() => NativeFontFace.Load(TestFonts.RobotoRegularBytes());

    private static GlyphPipeline PipelineFor(NativeFontFace face) =>
        new(new TrueTypeOutlineDecoder(face), face.Head.UnitsPerEm);

    // ---- Decode ----------------------------------------------------------------------------

    [Theory]
    [InlineData('A')]
    [InlineData('O')]
    [InlineData(0xE9)] // é — a composite in Roboto
    [InlineData(' ')]
    public void TrueTypeDecoder_ReturnsTheSameOutlineBoundsAndAdvanceAsTheGlyfPath(int codepoint)
    {
        var face = Roboto();
        int glyphIndex = face.GetGlyphIndex(codepoint);
        var parsed = face.GetGlyph(glyphIndex);
        var expected = OutlineExtractor.Extract(parsed);

        var decoded = new TrueTypeOutlineDecoder(face).Decode(glyphIndex, Size32);

        decoded.Outline.Commands.ShouldBe(expected.Commands);
        decoded.AdvanceWidth.ShouldBe(face.Hmtx.GetAdvanceWidth(glyphIndex));
        decoded.DeclaredBounds.ShouldBe(new OutlineBounds(parsed.XMin, parsed.YMin, parsed.XMax, parsed.YMax));
    }

    [Fact]
    public void OutlineDecoders_SelectsTheTrueTypeDecoder_ForAGlyfFace()
    {
        OutlineDecoders.ForFace(Roboto()).ShouldBeOfType<TrueTypeOutlineDecoder>();
    }

    [Fact]
    public void OutlineDecoders_SelectsTheCffDecoder_ForACffFace()
    {
        var face = NativeFontFace.Load(TestFonts.SourceSerifOtfBytes());
        OutlineDecoders.ForFace(face).ShouldBeOfType<CffOutlineDecoder>();
    }

    [Fact]
    public void OutlineDecoders_SelectsTheCffDecoder_ForACff2Face()
    {
        var face = NativeFontFace.Load(TestFonts.SourceCodeVariableOtfBytes());
        var decoder = OutlineDecoders.ForFace(face).ShouldBeOfType<CffOutlineDecoder>();
        decoder.Font.IsCff2.ShouldBeTrue();
    }

    [Fact]
    public void OutlineDecoders_MalformedCffTable_FailsTheLoad()
    {
        // The stub CFF table is a bare header: the decoder parses the table up front, so the
        // face fails here rather than rendering an atlas of nothing glyph by glyph.
        var face = NativeFontFace.Load(SyntheticFonts.CffFlavoured());
        Should.Throw<FontFormatException>(() => OutlineDecoders.ForFace(face));
    }

    // ---- Transform list --------------------------------------------------------------------

    [Fact]
    public void DefaultTransformList_IsEmpty()
    {
        GlyphTransforms.For(Size32).ShouldBeEmpty();
        GlyphTransforms.For(new RasterOptions { Size = 32, Bold = true, Italic = true }).ShouldBeEmpty();
    }

    [Fact]
    public void EmptyTransformList_IsIdentity()
    {
        var face = Roboto();
        int glyphIndex = face.GetGlyphIndex('g');
        var decoded = new TrueTypeOutlineDecoder(face).Decode(glyphIndex, Size32);

        var prepared = PipelineFor(face).Prepare(glyphIndex, Size32, []);

        prepared.Outline.Commands.ShouldBe(decoded.Outline.Commands);
        float scale = GlyphPipeline.PixelScale(Size32, face.Head.UnitsPerEm);
        prepared.ToPixels.ShouldBe(new OutlineTransform(scale, 0f));
        prepared.Box.ShouldBe(GlyphBox.Compute(decoded.Outline, decoded.DeclaredBounds, scale));
        prepared.Advance.ShouldBe((int)Math.Round(decoded.AdvanceWidth * (double)scale));
    }

    [Fact]
    public void Transforms_RunInOrder()
    {
        var face = Roboto();
        int glyphIndex = face.GetGlyphIndex('H');
        var log = new List<string>();

        PipelineFor(face).Prepare(glyphIndex, Size32, [new Recording("first", log), new Recording("second", log)]);

        log.ShouldBe(["first", "second"]);
    }

    [Fact]
    public void TranslatingTransform_MovesTheBitmapAndBearings_AndGrowsTheAdvance()
    {
        // Roboto has 2048 units/em, so at 32px one pixel is 64 units: a whole-pixel shift
        // keeps every coverage value bit-identical and only moves the bitmap.
        var face = Roboto();
        int glyphIndex = face.GetGlyphIndex('H');
        var pipeline = PipelineFor(face);
        var renderer = GlyphRenderers.Select(Size32);

        var plain = pipeline.Prepare(glyphIndex, Size32, []);
        var moved = pipeline.Prepare(glyphIndex, Size32, [new Translate(Dx: 128, Dy: -192, AdvanceGrowth: 256)]);

        moved.Box.BearingX.ShouldBe(plain.Box.BearingX + 2);
        moved.Box.BearingY.ShouldBe(plain.Box.BearingY - 3);
        moved.Box.Width.ShouldBe(plain.Box.Width);
        moved.Box.Height.ShouldBe(plain.Box.Height);
        moved.Advance.ShouldBe(plain.Advance + 4);

        var plainBitmap = renderer.Render('H', glyphIndex, plain, Size32);
        var movedBitmap = renderer.Render('H', glyphIndex, moved, Size32);
        movedBitmap.BitmapData.ShouldBe(plainBitmap.BitmapData);
        movedBitmap.Metrics.BearingX.ShouldBe(plainBitmap.Metrics.BearingX + 2);
        movedBitmap.Metrics.BearingY.ShouldBe(plainBitmap.Metrics.BearingY - 3);
        movedBitmap.Metrics.Advance.ShouldBe(plainBitmap.Metrics.Advance + 4);
        renderer.Measure(moved, Size32).ShouldBe(movedBitmap.Metrics);
    }

    [Fact]
    public void ReplacingTheOutline_DropsTheDeclaredBounds()
    {
        // A declared glyf bbox describes the untransformed outline only.
        var glyph = new DecodedGlyph(GlyphOutline.Empty, 500f, new OutlineBounds(0, 0, 10, 10));

        glyph.WithOutline(GlyphOutline.Empty).DeclaredBounds.ShouldBeNull();
        glyph.WithAdvanceWidth(600f).DeclaredBounds.ShouldBe(glyph.DeclaredBounds);
        glyph.WithAdvanceWidth(600f).AdvanceWidth.ShouldBe(600f);
    }

    // ---- Bounds ----------------------------------------------------------------------------

    [Fact]
    public void Bounds_ComeFromTheOutline_WhenTheDecoderDeclaresNone()
    {
        // A bbox-less source (CFF) must size its glyphs from the outline alone. The curve
        // peaks at y=200 while its controls reach ~267, so the tight bounds — not the control
        // hull — must set the height.
        var outline = GlyphOutline.FromCommands(
        [
            OutlineCommand.Move(0, 0),
            OutlineCommand.Cubic(0, 800f / 3f, 200, 800f / 3f, 200, 0),
            OutlineCommand.CloseAt(0, 0),
        ]);
        var decoder = new FixedDecoder(new DecodedGlyph(outline, 250f));

        var prepared = new GlyphPipeline(decoder, unitsPerEm: 1000).Prepare(1, new RasterOptions { Size = 100 }, []);

        prepared.Box.ShouldBe(new GlyphBox(MinX: 0, MinY: -20, Width: 20, Height: 20));
        prepared.Advance.ShouldBe(25);
    }

    // ---- Render-mode dispatch --------------------------------------------------------------

    [Fact]
    public void Dispatch_UsesCoverage_ForEveryAntiAliasMode()
    {
        foreach (var mode in Enum.GetValues<AntiAliasMode>())
            GlyphRenderers.Select(new RasterOptions { Size = 32, AntiAlias = mode }).ShouldBeOfType<CoverageRenderer>();
    }

    [Fact]
    public void Dispatch_RejectsColorAndSdf_WithTheExistingMessages()
    {
        Should.Throw<NotSupportedException>(() => GlyphRenderers.Select(new RasterOptions { Size = 32, ColorFont = true }))
            .Message.ShouldBe("Native rasterizer does not support color font rendering.");
        Should.Throw<NotSupportedException>(() => GlyphRenderers.Select(new RasterOptions { Size = 32, Sdf = true }))
            .Message.ShouldBe("Native rasterizer does not support SDF rendering.");

        // Color is checked first, as before the pipeline existed.
        Should.Throw<NotSupportedException>(() => GlyphRenderers.Select(new RasterOptions { Size = 32, ColorFont = true, Sdf = true }))
            .Message.ShouldContain("color", Case.Sensitive);
    }

    [Fact]
    public void Rasterizer_StillRejectsSdfAndColor_EvenForUnmappedCodepoints()
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());

        Should.Throw<NotSupportedException>(() => rasterizer.RasterizeGlyph(0x1F600, new RasterOptions { Size = 32, Sdf = true }));
        Should.Throw<NotSupportedException>(() => rasterizer.GetGlyphMetrics(0x1F600, new RasterOptions { Size = 32, ColorFont = true }));
    }

    // ---- Test doubles ----------------------------------------------------------------------

    private sealed record Translate(float Dx, float Dy, float AdvanceGrowth) : IGlyphTransform
    {
        public DecodedGlyph Apply(DecodedGlyph glyph)
        {
            var commands = glyph.Outline.Commands
                .Select(c => c with
                {
                    EndX = c.EndX + Dx,
                    EndY = c.EndY + Dy,
                    Ctrl1X = c.Ctrl1X + Dx,
                    Ctrl1Y = c.Ctrl1Y + Dy,
                    Ctrl2X = c.Ctrl2X + Dx,
                    Ctrl2Y = c.Ctrl2Y + Dy,
                })
                .ToArray();

            return glyph
                .WithOutline(GlyphOutline.FromCommands(commands))
                .WithAdvanceWidth(glyph.AdvanceWidth + AdvanceGrowth);
        }
    }

    private sealed class Recording(string name, List<string> log) : IGlyphTransform
    {
        public DecodedGlyph Apply(DecodedGlyph glyph)
        {
            log.Add(name);
            return glyph;
        }
    }

    private sealed class FixedDecoder(DecodedGlyph glyph) : IOutlineDecoder
    {
        public DecodedGlyph Decode(int glyphIndex, RasterOptions options) => glyph;
    }
}
