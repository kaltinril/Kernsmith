using KernSmith;
using KernSmith.Output;

namespace KernSmith.HintParity;

/// <summary>One glyph cut out of a generated atlas, with its <c>.fnt</c> metrics.</summary>
/// <param name="YOffset">The <c>.fnt</c> yoffset (line top to bitmap top).</param>
/// <param name="BaselineTop">yoffset minus <c>base</c>: bitmap top relative to the baseline, so boxes
/// compare physically even when the two backends disagree on <c>base</c>.</param>
internal sealed record GlyphBitmap(
    int Id, int Width, int Height, int XOffset, int YOffset, int BaselineTop, int XAdvance, byte[] Pixels)
{
    public bool IsEmpty => Width == 0 || Height == 0;

    public byte At(int x, int y) => Pixels[(y * Width) + x];
}

/// <summary>A font generated through the public <see cref="BmFont.Generate(byte[], FontGeneratorOptions?)"/> path.</summary>
internal sealed class GeneratedFont
{
    public required int LineHeight { get; init; }
    public required int Base { get; init; }
    public required IReadOnlyDictionary<int, GlyphBitmap> Glyphs { get; init; }
    public required TimeSpan Elapsed { get; init; }

    public static GeneratedFont Generate(byte[] fontData, FontGeneratorOptions options)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        BmFontResult result = BmFont.Generate(fontData, options);
        watch.Stop();

        var pages = result.Pages.Select(p => (p.Width, Alpha: p.GetAlpha8PixelData())).ToArray();
        var glyphs = new Dictionary<int, GlyphBitmap>();
        int baseline = result.Model.Common.Base;

        foreach (var c in result.Model.Characters)
        {
            var pixels = new byte[c.Width * c.Height];
            if (c.Width > 0 && c.Height > 0)
            {
                var (pageWidth, alpha) = pages[c.Page];
                for (int y = 0; y < c.Height; y++)
                    Array.Copy(alpha, ((c.Y + y) * pageWidth) + c.X, pixels, y * c.Width, c.Width);
            }

            glyphs[c.Id] = new GlyphBitmap(c.Id, c.Width, c.Height, c.XOffset, c.YOffset, c.YOffset - baseline, c.XAdvance, pixels);
        }

        return new GeneratedFont
        {
            LineHeight = result.Model.Common.LineHeight,
            Base = baseline,
            Glyphs = glyphs,
            Elapsed = watch.Elapsed,
        };
    }
}
