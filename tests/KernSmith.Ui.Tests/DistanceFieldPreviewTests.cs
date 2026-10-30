using KernSmith.Atlas;
using KernSmith.Output.Model;
using KernSmith.Ui.Models;
using Shouldly;

namespace KernSmith.Ui.Tests;

/// <summary>
/// The Sample tab decodes distance-field pages to coverage. An MSDF page's RGB are distances,
/// so drawing it as color shows opaque rainbow boxes instead of glyphs.
/// </summary>
public sealed class DistanceFieldPreviewTests
{
    private static AtlasPage Gray(params byte[] pixels) => new()
    {
        PageIndex = 0,
        Width = pixels.Length,
        Height = 1,
        PixelData = pixels,
        Format = PixelFormat.Grayscale8,
    };

    private static AtlasPage Rgba(params byte[] pixels) => new()
    {
        PageIndex = 0,
        Width = pixels.Length / 4,
        Height = 1,
        PixelData = pixels,
        Format = PixelFormat.Rgba32,
    };

    private static byte[] Premultiplied(params byte[] coverage) =>
        coverage.SelectMany(a => new[] { a, a, a, a }).ToArray();

    [Fact]
    public void PlainBitmap_IsNotDecoded()
    {
        DistanceFieldPreview.DecodeToCoverage(Gray(0, 128, 255), null).ShouldBeNull();
        DistanceFieldPreview.DecodeToCoverage(Gray(0, 128, 255), new ExtendedMetadata { GeneratorVersion = "test" }).ShouldBeNull();
    }

    [Fact]
    public void SingleChannelSdf_DecodesToCoverageAroundTheEdge()
    {
        // Encoding is 128 +/- |d| * 128 / spread (inside bright). With spread 8, one code value is
        // 1/16 px: 128 is the edge (half covered), 132 is 0.25 px inside, 140 fully inside.
        var decoded = DistanceFieldPreview.DecodeToCoverage(
            Gray(0, 120, 128, 132, 140, 255), new ExtendedMetadata { GeneratorVersion = "test", SdfSpread = 8 });

        decoded.ShouldBe(Premultiplied(0, 0, 128, 191, 255, 255));
    }

    [Fact]
    public void Msdf_DecodesTheMedianOfRgb()
    {
        // median(200, 128, 60) = 128 -> edge; median(255, 255, 0) = 255 -> inside;
        // median(0, 0, 255) = 0 -> outside. Alpha (255 for MSDF) is not the shape.
        var decoded = DistanceFieldPreview.DecodeToCoverage(
            Rgba(200, 128, 60, 255, 255, 255, 0, 255, 0, 0, 255, 255),
            new ExtendedMetadata { GeneratorVersion = "test", SdfSpread = 8, SdfMode = SdfMode.Msdf });

        decoded.ShouldBe(Premultiplied(128, 255, 0));
    }

    [Fact]
    public void Mtsdf_DecodesTheMedianOfRgbNotAlpha()
    {
        var decoded = DistanceFieldPreview.DecodeToCoverage(
            Rgba(255, 255, 0, 0),
            new ExtendedMetadata { GeneratorVersion = "test", SdfSpread = 8, SdfMode = SdfMode.Mtsdf });

        decoded.ShouldBe(Premultiplied(255));
    }

    [Fact]
    public void SingleChannelSdfWithColorGlyphs_IsNotDecoded()
    {
        // SDF + ColorFont keeps color glyphs in color on an RGBA page; decoding would erase them.
        DistanceFieldPreview.DecodeToCoverage(Rgba(255, 0, 0, 255), new ExtendedMetadata { GeneratorVersion = "test", SdfSpread = 8 })
            .ShouldBeNull();
    }
}
