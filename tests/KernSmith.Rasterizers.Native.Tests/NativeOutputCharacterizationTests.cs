using System.Security.Cryptography;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Pins Native's exact output — bitmaps, bearings, advances and <c>GetGlyphMetrics</c> — for
/// a representative Roboto glyph set, so restructuring the glyph pipeline cannot change a
/// single byte unnoticed. A legitimate pixel change must update them deliberately; the last one
/// made the default request hint at <see cref="HintLevel.Normal"/> with its size taper, which
/// changed every hash up to 32 ppem and left 42.7 ppem (32pt at 96 dpi) and 72 ppem alone.
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
    [InlineData(8f, 72, AntiAliasMode.Grayscale, "7D065D9B5083200A75C9F044A6D68922634100236211EA489EF15EDD0160E67A")]
    [InlineData(12f, 72, AntiAliasMode.Grayscale, "2930088ECE9DB8344E8595B3DEFDDF39DB40A1FEC6FBAE01114239DF761E8CD5")]
    [InlineData(12f, 72, AntiAliasMode.None, "78B2CD1A039BAB94601CA4FA3325BA82C73235D8862A862065A85802E3E5FA3F")]
    [InlineData(17.5f, 72, AntiAliasMode.Grayscale, "6FF40DAD5B56A326B2434000829163439BF7AB22EFAB7A8A9A123312A86CFDC5")]
    [InlineData(32f, 72, AntiAliasMode.Grayscale, "FD51ADE361D863071469A69B32517486B33787E706AC7C1CEC8D6E823C01421E")]
    [InlineData(32f, 96, AntiAliasMode.None, "B4991B8DAB0716E75F041B3BB1898F3627C8248721B62D4A5363571B20AE0264")]
    [InlineData(72f, 72, AntiAliasMode.Grayscale, "696D393750D9D21C2A74891E6E7B909448D22026080891ED9FFCD166A9F88C00")]
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
