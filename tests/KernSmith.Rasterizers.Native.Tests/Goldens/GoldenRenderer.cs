using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;

namespace KernSmith.Rasterizers.Native.Tests.Goldens;

/// <summary>One rendered glyph of a golden case: the bitmap (null when unmapped), the
/// <c>GetGlyphMetrics</c> answer, and an 8-hex-digit fingerprint of both.</summary>
internal sealed record GoldenGlyph(int Codepoint, RasterizedGlyph? Glyph, GlyphMetrics? Measured, string Fingerprint)
{
    public string Label => $"U+{Codepoint:X4}";

    public string Describe()
    {
        string text = char.IsControl((char)Math.Min(Codepoint, 0xFFFF)) || Codepoint > 0xFFFF ? string.Empty
            : $" '{char.ConvertFromUtf32(Codepoint)}'";
        if (Glyph is null)
            return $"{Label}{text} (unmapped)";

        var m = Glyph.Metrics;
        return string.Create(CultureInfo.InvariantCulture,
            $"{Label}{text} now {Glyph.Width}x{Glyph.Height} {Glyph.Format} at ({m.BearingX},{m.BearingY}) advance {m.Advance}");
    }
}

/// <summary>The result of rendering a golden case: the manifest entry and what a failure report needs.</summary>
internal sealed record GoldenResult(GoldenEntry Entry, IReadOnlyList<GoldenGlyph> Glyphs, GoldenImage Sheet);

/// <summary>The result of a pipeline-level golden: normalized <c>.fnt</c> text and atlas pages.</summary>
internal sealed record GenerateGoldenResult(GoldenEntry Entry, string Fnt, IReadOnlyList<GoldenImage> Pages);

/// <summary>Renders golden cases and hashes them.</summary>
internal static class GoldenRenderer
{
    /// <summary>Rasterizes every codepoint of <paramref name="goldenCase"/> through a fresh
    /// <see cref="NativeRasterizer"/> and fingerprints each glyph's bytes and metrics.</summary>
    public static GoldenResult Render(GoldenCase goldenCase)
    {
        var bytes = goldenCase.Font.Bytes();
        using var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(bytes);
        if (goldenCase.Axes is not null)
        {
            var fvar = BmFont.ReadFontInfo(bytes).VariationAxes
                ?? throw new InvalidOperationException($"{goldenCase.Font.Id} is not a variable font");
            rasterizer.SetVariationAxes(fvar, goldenCase.Axes);
        }

        if (goldenCase.Palette != 0)
            rasterizer.SelectColorPalette(goldenCase.Palette);

        var glyphs = new List<GoldenGlyph>();
        using var caseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (int codepoint in goldenCase.Font.Codepoints())
        {
            var glyph = rasterizer.RasterizeGlyph(codepoint, goldenCase.Options);
            var measured = rasterizer.GetGlyphMetrics(codepoint, goldenCase.Options);
            var record = Serialize(codepoint, glyph, measured);
            caseHash.AppendData(record);
            glyphs.Add(new GoldenGlyph(codepoint, glyph, measured, Fingerprint(record)));
        }

        string parts = string.Join(' ', glyphs.Select(g => $"{g.Label}={g.Fingerprint}"));
        var sheet = GoldenImage.ContactSheet(glyphs.Where(g => g.Glyph is not null).Select(g => g.Glyph!).ToList());
        return new GoldenResult(new GoldenEntry(Convert.ToHexString(caseHash.GetHashAndReset()), parts), glyphs, sheet);
    }

    /// <summary>Runs <see cref="BmFont.Generate(byte[], FontGeneratorOptions)"/> on
    /// <paramref name="fontBytes"/> (the case's font, or a WOFF2 of it).</summary>
    public static GenerateGoldenResult Generate(GenerateCase goldenCase, byte[] fontBytes)
    {
        var result = BmFont.Generate(fontBytes, goldenCase.Options());
        string fnt = NormalizeFnt(result.FntText);

        var fntBytes = Encoding.UTF8.GetBytes(fnt);
        using var caseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        caseHash.AppendData(fntBytes);
        var parts = new List<string> { $"fnt={Fingerprint(fntBytes)}" };
        var pages = new List<GoldenImage>();
        foreach (var page in result.Pages)
        {
            int channels = page.Format == PixelFormat.Rgba32 ? 4 : 1;
            var image = new GoldenImage(page.Width, page.Height, channels, page.PixelData);
            using var pageStream = new MemoryStream();
            using (var writer = new BinaryWriter(pageStream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(page.PageIndex);
                writer.Write(page.Width);
                writer.Write(page.Height);
                writer.Write((int)page.Format);
                writer.Write(page.PixelData.Length);
                writer.Write(page.PixelData);
            }

            var record = pageStream.ToArray();
            caseHash.AppendData(record);
            parts.Add($"page{page.PageIndex}={Fingerprint(record)}");
            pages.Add(image);
        }

        return new GenerateGoldenResult(
            new GoldenEntry(Convert.ToHexString(caseHash.GetHashAndReset()), string.Join(' ', parts)), fnt, pages);
    }

    /// <summary>
    /// The <c>.fnt</c> text with LF line endings and the generator version blanked, so a
    /// version bump or a Windows newline does not count as drift.
    /// </summary>
    public static string NormalizeFnt(string fnt) =>
        GeneratorVersion.Replace(fnt.Replace("\r\n", "\n", StringComparison.Ordinal), "kernsmith version=\"*\"");

    private static byte[] Serialize(int codepoint, RasterizedGlyph? glyph, GlyphMetrics? measured)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(codepoint);
            writer.Write(glyph is not null);
            if (glyph is not null)
            {
                writer.Write(glyph.GlyphIndex);
                writer.Write(glyph.Width);
                writer.Write(glyph.Height);
                writer.Write(glyph.Pitch);
                writer.Write((int)glyph.Format);
                WriteMetrics(writer, glyph.Metrics);
                writer.Write(glyph.BitmapData.Length);
                writer.Write(glyph.BitmapData);
            }

            writer.Write(measured.HasValue);
            if (measured.HasValue)
                WriteMetrics(writer, measured.Value);
        }

        return buffer.ToArray();
    }

    private static void WriteMetrics(BinaryWriter writer, GlyphMetrics metrics)
    {
        writer.Write(metrics.BearingX);
        writer.Write(metrics.BearingY);
        writer.Write(metrics.Advance);
        writer.Write(metrics.Width);
        writer.Write(metrics.Height);
    }

    /// <summary>The first four bytes of the SHA-256, as hex: enough to say which glyph changed;
    /// the case's full SHA-256 is what decides pass or fail.</summary>
    private static string Fingerprint(byte[] record) =>
        Convert.ToHexString(SHA256.HashData(record), 0, 4).ToLowerInvariant();

    // A runtime Regex, not [GeneratedRegex]: the regex source generator's output trips CodeQL
    // quality queries (see project_decisions.md, CodeQL Configuration).
    private static readonly Regex GeneratorVersion = new("kernsmith version=\"[^\"]*\"");
}
