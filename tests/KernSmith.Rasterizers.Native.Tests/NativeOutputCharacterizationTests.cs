using System.Security.Cryptography;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Pins Native's exact output — bitmaps, bearings, advances and <c>GetGlyphMetrics</c> — for
/// a representative Roboto glyph set, so restructuring the glyph pipeline cannot change a
/// single byte unnoticed. A legitimate pixel change must update them deliberately; the last one
/// cropped <see cref="AntiAliasMode.None"/> glyph boxes to their ink, which changed only the
/// None hashes: most glyphs lose an empty edge row or column, with every lit pixel's pen-
/// relative position and advance unchanged.
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
    [InlineData(8f, 72, AntiAliasMode.Grayscale, "4D45F8102FEF1A7D13E9D33640CB067E6FA2DAE9FB6F4F12EA366C6EA7A64087")]
    [InlineData(12f, 72, AntiAliasMode.Grayscale, "407F35FD83545B5E2A5B7C395075E1284723205FA386BD5A7D0C242979238914")]
    [InlineData(12f, 72, AntiAliasMode.None, "7BBCA214C44A67D88D636FABCF2499B0374D73FCBDA92FCA80BE1D2D51C1439D")]
    [InlineData(17.5f, 72, AntiAliasMode.Grayscale, "4F0B04FD8DE616F6321E85941F42ADD2A09F3731D1DBFDE246F1C2C3B842301A")]
    [InlineData(32f, 72, AntiAliasMode.Grayscale, "598F0AEFEFE0E3B6B7EC62DE40E88D6DBE1547F24E30913DD6CE6464D2FA36DD")]
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
