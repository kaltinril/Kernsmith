using System.Security.Cryptography;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Pins Native's exact output — bitmaps, bearings, advances and <c>GetGlyphMetrics</c> — for
/// a representative Roboto glyph set, so restructuring the glyph pipeline cannot change a
/// single byte unnoticed. The expected hashes are the output of the pre-pipeline straight-line
/// <c>RasterizeGlyph</c>; a legitimate pixel change must update them deliberately.
/// </summary>
public class NativeOutputCharacterizationTests
{
    /// <summary>Printable ASCII and Latin-1 (accented letters are composites in Roboto),
    /// plus an unmapped codepoint.</summary>
    private static readonly int[] GlyphSet =
    [
        .. Enumerable.Range(0x20, 0x7F - 0x20),
        .. Enumerable.Range(0xA0, 0x100 - 0xA0),
        0x1F600,
    ];

    [Theory]
    [InlineData(8f, 72, AntiAliasMode.Grayscale, "0E7DDC883CDAD2AF4B52616BBECC1DEAA3F827D73D9239929623D77D14190AC2")]
    [InlineData(12f, 72, AntiAliasMode.Grayscale, "77682BC8D974103F73ADB0BB974823DE4F5256BB286E3CB8B38E3D382BBA1854")]
    [InlineData(12f, 72, AntiAliasMode.None, "202C4B3E43B526E5BD990A71547CB636A5D8F63B922D2237A4AFB20651B54844")]
    [InlineData(17.5f, 72, AntiAliasMode.Grayscale, "24B4A0742E24CE0ABF9C1B6A9ABC8511F8CC28B18F4C0ACD0A060FEB90B6F3C8")]
    [InlineData(32f, 72, AntiAliasMode.Grayscale, "48680CB856880720F11C5334FF7BC553FCAFA97A15E46EEBFAACD09B411CA4A9")]
    [InlineData(32f, 96, AntiAliasMode.None, "FF1DCE398C1058C315094167F01091675259631BFA93363F28672A733955ED8D")]
    [InlineData(72f, 72, AntiAliasMode.Grayscale, "0225AEDEE6CF108A6A8BE34A71D37A8D4242E328A474340564968A3DF856DBD5")]
    public void Output_IsByteIdenticalToTheCharacterizedBaseline(
        float size, int dpi, AntiAliasMode antiAlias, string expectedHash)
    {
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        var options = new RasterOptions { Size = size, Dpi = dpi, AntiAlias = antiAlias };

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (int codepoint in GlyphSet)
            {
                writer.Write(codepoint);

                var glyph = rasterizer.RasterizeGlyph(codepoint, options);
                if (glyph is null)
                {
                    writer.Write(false);
                }
                else
                {
                    writer.Write(true);
                    writer.Write(glyph.GlyphIndex);
                    writer.Write(glyph.Width);
                    writer.Write(glyph.Height);
                    writer.Write(glyph.Pitch);
                    writer.Write((int)glyph.Format);
                    WriteMetrics(writer, glyph.Metrics);
                    writer.Write(glyph.BitmapData.Length);
                    writer.Write(glyph.BitmapData);
                }

                var metrics = rasterizer.GetGlyphMetrics(codepoint, options);
                writer.Write(metrics.HasValue);
                if (metrics.HasValue)
                    WriteMetrics(writer, metrics.Value);
            }
        }

        string actual = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        actual.ShouldBe(expectedHash, $"Native output changed at {size}pt/{dpi}dpi/{antiAlias}");
    }

    [Fact]
    public void GlyphSet_CoversCompositeEmptyAndUnmappedGlyphs()
    {
        // Guards the characterization: without these the hashes above would not exercise
        // composite resolution, the blank-glyph path or the unmapped-codepoint path.
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        var face = rasterizer.Face!;

        var mapped = GlyphSet.Select(rasterizer.GetGlyphIndex).Where(i => i != 0).ToArray();
        mapped.ShouldContain(i => face.GetGlyph(i).IsComposite, "no composite glyph in the set");
        mapped.ShouldContain(i => face.GetGlyph(i).IsEmpty, "no empty glyph in the set");
        GlyphSet.ShouldContain(cp => rasterizer.GetGlyphIndex(cp) == 0, "no unmapped codepoint in the set");
    }

    private static void WriteMetrics(BinaryWriter writer, Font.Models.GlyphMetrics metrics)
    {
        writer.Write(metrics.BearingX);
        writer.Write(metrics.BearingY);
        writer.Write(metrics.Advance);
        writer.Write(metrics.Width);
        writer.Write(metrics.Height);
    }
}
