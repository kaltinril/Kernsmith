using KernSmith.Output;
using KernSmith.Output.Model;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// <see cref="FontGeneratorOptions.EqualizeCellHeights"/> is BMFont's <c>useFixedHeight</c>: every
/// cell spans the line box (yoffset 0, height lineHeight, the baseline at row <c>base</c> in every
/// cell), grown to cover any glyph reaching outside it — the same top and height for every char.
/// bmfont64 with useFixedHeight=1 (Arial 32): every char yoffset=0 height=32.
/// </summary>
public sealed class EqualizeCellHeightsTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static BmFontResult Generate(bool equalize, string chars = "AaxgÅ|.", int padding = 0) =>
        BmFont.Generate(Font(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(chars),
            Rasterizer = new StbTrueTypeRasterizer(),
            EqualizeCellHeights = equalize,
            Padding = new Padding(padding),
        });

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void EveryCell_SpansTheLineBoxGrownToItsGlyphs(int padding)
    {
        var plain = Generate(equalize: false, padding: padding).Model;
        var equalized = Generate(equalize: true, padding: padding).Model;

        // The union of the line box [0, lineHeight) and every glyph's ink rows.
        var lineHeight = plain.Common.LineHeight;
        var inked = plain.Characters.Where(c => c.Height > 2 * padding).ToList();
        var top = Math.Min(0, inked.Min(c => c.YOffset + padding));
        var bottom = Math.Max(lineHeight, inked.Max(c => c.YOffset + c.Height - padding));

        equalized.Common.LineHeight.ShouldBe(lineHeight);
        equalized.Common.Base.ShouldBe(plain.Common.Base);
        foreach (var c in equalized.Characters)
        {
            c.YOffset.ShouldBe(top - padding, $"'{(char)c.Id}'");
            c.Height.ShouldBe(bottom - top + 2 * padding, $"'{(char)c.Id}'");
        }
    }

    [Fact]
    public void GlyphInk_StaysWhereThePlainFontDrawsIt()
    {
        var plain = Generate(equalize: false);
        var equalized = Generate(equalize: true);

        foreach (var ch in "AaxgÅ|.")
        {
            InkRows(equalized, ch).ShouldBe(InkRows(plain, ch), $"'{ch}'");
        }
    }

    /// <summary>Each inked row's coverage, keyed by its pen-relative y (yoffset applied).</summary>
    private static Dictionary<int, string> InkRows(BmFontResult result, char ch)
    {
        var page = result.Pages[0];
        var bpp = page.Format == PixelFormat.Rgba32 ? 4 : 1;
        var c = result.Model.Characters.Single(e => e.Id == ch);
        var rows = new Dictionary<int, string>();
        for (var y = 0; y < c.Height; y++)
        {
            var row = Enumerable.Range(0, c.Width)
                .Select(x => page.PixelData[((c.Y + y) * page.Width + c.X + x) * bpp + (bpp - 1)]).ToArray();
            if (row.Any(v => v != 0))
                rows[y + c.YOffset] = string.Join(",", row);
        }

        return rows;
    }
}
