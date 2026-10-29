using System.Security.Cryptography;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Pins Native's exact output — bitmaps, bearings, advances and <c>GetGlyphMetrics</c> — for
/// a representative Roboto glyph set, so restructuring the glyph pipeline cannot change a
/// single byte unnoticed. A legitimate pixel change must update them deliberately; the last one
/// added mono dropout control, which changed only the <see cref="AntiAliasMode.None"/> hashes
/// and only by adding pixels where a scanline through pixel centres crossed ink yet lit nothing.
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
    [InlineData(8f, 72, AntiAliasMode.Grayscale, "816AE03FF4BC9ED70C41DC699453CCF402ED6E852C22C7DD686105546E15ED46")]
    [InlineData(12f, 72, AntiAliasMode.Grayscale, "85C329840396C0397B655BFEC2A8DFB60D24BA8C09779E529287B8E48181077A")]
    [InlineData(12f, 72, AntiAliasMode.None, "DB938F3774AC59B43714559A376088DA0672AE927517B94F56F85C6CE3973AE4")]
    [InlineData(17.5f, 72, AntiAliasMode.Grayscale, "1F83647C6FA383B08FE64A4D4109AF4D530047FE8DB26B5782D0CD282AF3CAA4")]
    [InlineData(32f, 72, AntiAliasMode.Grayscale, "C66FC4D8E539E493ADC22E29BEC7B9236A243A26B13691C8AC40A12FA3F65CFF")]
    [InlineData(32f, 96, AntiAliasMode.None, "A7CD8AFC71351BCE144EF3619EDFEB5723DCD8F3BE57E45F56EB8171696ED250")]
    [InlineData(72f, 72, AntiAliasMode.Grayscale, "525E9D0DC1A0D7747D6D020FFF779D357DDAF57C973F1D337CEAC8040AA51CF4")]
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
