using KernSmith;
using KernSmith.Atlas;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Atlas;

/// <summary>
/// Tests for <see cref="ChannelCompositor"/> per-channel content resolution,
/// covering color preservation (Glyph content reads the matching RGB component
/// from color glyphs) and white-fill (One content yields 255).
/// </summary>
public class ChannelCompositorTests
{
    private sealed class FakeEncoder : IAtlasEncoder
    {
        public string FileExtension => ".png";
        public byte[] Encode(byte[] pixelData, int width, int height, PixelFormat format) => Array.Empty<byte>();
    }

    /// <summary>Builds a 1x1 RGBA glyph with the given component values.</summary>
    private static RasterizedGlyph MakeRgbaGlyph(byte r, byte g, byte b, byte a)
    {
        return new RasterizedGlyph
        {
            Codepoint = 65,
            GlyphIndex = 1,
            BitmapData = new[] { r, g, b, a },
            Width = 1,
            Height = 1,
            Pitch = 4,
            Metrics = new GlyphMetrics(BearingX: 0, BearingY: 1, Advance: 1, Width: 1, Height: 1),
            Format = PixelFormat.Rgba32
        };
    }

    private static PackResult OneGlyphPack() => new()
    {
        Placements = new[] { new GlyphPlacement(Id: 65, PageIndex: 0, X: 0, Y: 0) },
        PageCount = 1,
        PageWidth = 1,
        PageHeight = 1
    };

    private static (byte r, byte g, byte b, byte a) CompositePixel(
        RasterizedGlyph glyph, ChannelConfig config, IReadOnlyList<RasterizedGlyph>? outlineGlyphs = null)
    {
        var pages = ChannelCompositor.Build(
            new[] { glyph },
            outlineGlyphs,
            OneGlyphPack(),
            new Padding(0, 0, 0, 0),
            config,
            new FakeEncoder());

        var data = pages[0].PixelData;
        return (data[0], data[1], data[2], data[3]);
    }

    // == Color preservation (Bug 1) ==========================================

    [Fact]
    public void GlyphContent_PreservesDistinctRgbComponents_FromColorGlyph()
    {
        // A reddish, fully-opaque color glyph pixel.
        var glyph = MakeRgbaGlyph(r: 200, g: 40, b: 0, a: 255);

        // Glyph in R, G, B; One in A (typical color-font channel config).
        var config = new ChannelConfig(
            Alpha: ChannelContent.One,
            Red: ChannelContent.Glyph,
            Green: ChannelContent.Glyph,
            Blue: ChannelContent.Glyph);

        var (r, g, b, a) = CompositePixel(glyph, config);

        // The gradient color must survive: NOT flattened to R==G==B==coverage.
        r.ShouldBe((byte)200);
        g.ShouldBe((byte)40);
        b.ShouldBe((byte)0);
        a.ShouldBe((byte)255);
    }

    [Fact]
    public void GlyphContent_OnGrayscaleGlyph_UsesCoverageForAllChannels()
    {
        // Grayscale source: coverage byte should fill every Glyph channel (unchanged behavior).
        var glyph = new RasterizedGlyph
        {
            Codepoint = 65,
            GlyphIndex = 1,
            BitmapData = new byte[] { 180 },
            Width = 1,
            Height = 1,
            Pitch = 1,
            Metrics = new GlyphMetrics(BearingX: 0, BearingY: 1, Advance: 1, Width: 1, Height: 1),
            Format = PixelFormat.Grayscale8
        };

        var config = new ChannelConfig(
            Alpha: ChannelContent.Glyph,
            Red: ChannelContent.Glyph,
            Green: ChannelContent.Glyph,
            Blue: ChannelContent.Glyph);

        var (r, g, b, a) = CompositePixel(glyph, config);

        r.ShouldBe((byte)180);
        g.ShouldBe((byte)180);
        b.ShouldBe((byte)180);
        a.ShouldBe((byte)180);
    }

    // == Phantom outline (Bug 2) =============================================

    [Fact]
    public void OutlineContent_WithNoOutlineGlyphs_FallsBackToGlyphCoverage()
    {
        // plain.bmfc scenario: alphaChnl=Outline but outlineThickness=0, so no
        // outline layer is generated (outlineGlyphs == null). The alpha channel must
        // hold the glyph's own coverage, NOT a synthesized outline and NOT empty/0.
        var glyph = new RasterizedGlyph
        {
            Codepoint = 65,
            GlyphIndex = 1,
            BitmapData = new byte[] { 200 },
            Width = 1,
            Height = 1,
            Pitch = 1,
            Metrics = new GlyphMetrics(BearingX: 0, BearingY: 1, Advance: 1, Width: 1, Height: 1),
            Format = PixelFormat.Grayscale8
        };

        var config = new ChannelConfig(
            Alpha: ChannelContent.Outline,
            Red: ChannelContent.One,
            Green: ChannelContent.One,
            Blue: ChannelContent.One);

        var (_, _, _, a) = CompositePixel(glyph, config, outlineGlyphs: null);

        // Falls back to glyph coverage rather than 0 (empty) or a synthetic outline value.
        a.ShouldBe((byte)200);
    }

    // == White-fill (One content) ============================================

    [Fact]
    public void OneContent_YieldsWhite()
    {
        var glyph = MakeRgbaGlyph(r: 200, g: 40, b: 0, a: 255);

        var config = new ChannelConfig(
            Alpha: ChannelContent.Glyph,
            Red: ChannelContent.One,
            Green: ChannelContent.Glyph,
            Blue: ChannelContent.Glyph);

        var (r, _, _, _) = CompositePixel(glyph, config);

        r.ShouldBe((byte)255);
    }
}
