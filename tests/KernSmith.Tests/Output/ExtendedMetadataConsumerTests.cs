using KernSmith.Output;
using KernSmith.Rasterizers.StbTrueType;
using RenderingLibrary.Graphics;
using Shouldly;

namespace KernSmith.Tests.Output;

/// <summary>
/// Readers must take a <c>.fnt</c> with or without KernSmith's extended metadata alike: Gum's
/// BMFont parser (<see cref="ParsedFontFile"/>, what Gum and its KernSmith integrations load
/// fonts with) ignores the <c>kernsmith</c> line, and a binary reader skips any block by its size.
/// </summary>
public sealed class ExtendedMetadataConsumerTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static BmFontResult GenerateWithExtended() => BmFont.Generate(Font(), new FontGeneratorOptions
    {
        Size = 24,
        Characters = CharacterSet.FromChars("AVWa"),
        Rasterizer = new StbTrueTypeRasterizer(),
        FallbackCharacter = '?',
    });

    [Fact]
    public void GumParser_ReadsTheSameGlyphsWithAndWithoutTheKernSmithLine()
    {
        var withLine = GenerateWithExtended().FntText;
        withLine.ShouldContain("\nkernsmith ", Case.Sensitive);
        var withoutLine = string.Join("\n", withLine.Split('\n').Where(l => !l.StartsWith("kernsmith", StringComparison.Ordinal)));

        var a = new ParsedFontFile(withLine);
        var b = new ParsedFontFile(withoutLine);

        a.Chars.Count.ShouldBeGreaterThan(0);
        Chars(a).ShouldBe(Chars(b));
        a.Kernings.Select(k => (k.First, k.Second, k.Amount)).ShouldBe(b.Kernings.Select(k => (k.First, k.Second, k.Amount)));
        (a.Common!.LineHeight, a.Common.Base).ShouldBe((b.Common!.LineHeight, b.Common.Base));
        a.Pages.Select(p => p.File).ShouldBe(b.Pages.Select(p => p.File));
    }

    [Fact]
    public void BinaryReader_SkipsAnUnknownBlockBySize()
    {
        var fnt = GenerateWithExtended().FntBinary;
        // Insert a block of an unknown type (99) with a 7-byte payload after the header.
        byte[] unknown = [99, 7, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7];
        var patched = fnt.Take(4).Concat(unknown).Concat(fnt.Skip(4)).ToArray();

        var expected = BmFontReader.ReadBinary(fnt);
        var actual = BmFontReader.ReadBinary(patched);

        actual.Characters.ShouldBe(expected.Characters);
        actual.KerningPairs.ShouldBe(expected.KerningPairs);
        actual.Common.ShouldBe(expected.Common);
        actual.Extended!.FallbackCharacter.ShouldBe(expected.Extended!.FallbackCharacter);
    }

    private static List<(int, int, int, int, int, int, int, int, int)> Chars(ParsedFontFile f) =>
        f.Chars.Select(c => (c.Id, c.X, c.Y, c.Width, c.Height, c.XOffset, c.YOffset, c.XAdvance, c.Page)).ToList();
}
