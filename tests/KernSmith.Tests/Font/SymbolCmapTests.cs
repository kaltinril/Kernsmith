using KernSmith.Font;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Font;

/// <summary>
/// Fonts whose only <c>cmap</c> subtable is the Windows symbol one (platform 3, encoding 0) —
/// Wingdings, Webdings, Symbol — map their glyphs at U+F020-U+F0FF. BMFont (SYMBOL charset)
/// writes each glyph at both that code and its byte decoded as Windows-1252 (bmfont64, Wingdings
/// 32: 446 chars, 223 at U+F0xx and 223 low — 0x80 as U+20AC, 0x99 as U+2122 — and no kerning),
/// so KernSmith exposes both on every backend.
/// </summary>
[Collection("RasterizerFactory")]
public sealed class SymbolCmapTests
{
    private static byte[] SystemFont(string file)
    {
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
        Assert.SkipUnless(File.Exists(path), $"{file} is not installed on this machine.");
        return File.ReadAllBytes(path);
    }

    [Theory]
    [InlineData("wingding.ttf")]
    [InlineData("symbol.ttf")]
    [InlineData("webdings.ttf")]
    public void Reader_ExposesEachSymbolGlyphAtBothCodes(string file)
    {
        var info = new TtfFontReader().ReadFont(SystemFont(file));

        var high = info.AvailableCodepoints.Where(cp => cp is >= 0xF000 and <= 0xF0FF).ToList();
        var low = info.AvailableCodepoints.Where(cp => cp is < 0xF000 or > 0xF0FF).ToList();
        high.ShouldNotBeEmpty();
        low.ShouldBe(high.Select(SymbolEncoding.ToUnicode).ToList(), ignoreOrder: true);
        foreach (var cp in low)
            SymbolEncoding.ToSymbolCode(cp).ShouldNotBeNull($"U+{cp:X4}");
    }

    [Fact]
    public void Wingdings_HasBmFontsCharacters()
    {
        // bmfont64 -c (fontName=Wingdings, charSet=2, chars=0-65535): 446 chars; its 0x80-0x9F
        // bytes come out as their Windows-1252 characters.
        var codepoints = new TtfFontReader().ReadFont(SystemFont("wingding.ttf")).AvailableCodepoints;

        codepoints.Count.ShouldBe(446);
        codepoints.ShouldContain(0x20AC);          // 0x80
        codepoints.ShouldContain(0x2122);          // 0x99
        codepoints.ShouldContain(0x81);            // undefined in Windows-1252: kept as is
        codepoints.ShouldNotContain(0x80);
        codepoints.ShouldContain(0x4A);
    }

    [Theory]
    [InlineData(0x41, 0xF041)]
    [InlineData(0xFF, 0xF0FF)]
    [InlineData(0x20AC, 0xF080)]
    [InlineData(0x2122, 0xF099)]
    [InlineData(0x81, 0xF081)]
    public void SymbolEncoding_MapsWindows1252ToTheSymbolCode(int codepoint, int symbolCode)
    {
        SymbolEncoding.ToSymbolCode(codepoint).ShouldBe(symbolCode);
        SymbolEncoding.ToUnicode(symbolCode).ShouldBe(codepoint);
    }

    [Theory]
    [InlineData(0x80)]   // its byte decodes to U+20AC instead
    [InlineData(0x100)]
    [InlineData(0xF041)] // already a symbol code
    [InlineData(-1)]
    public void SymbolEncoding_HasNoSymbolCodeOutsideWindows1252(int codepoint) =>
        SymbolEncoding.ToSymbolCode(codepoint).ShouldBeNull();

    [Fact]
    public void IsSymbolFont_IsTrueOnlyForASymbolOnlyCmap()
    {
        SymbolEncoding.IsSymbolFont(SystemFont("wingding.ttf")).ShouldBeTrue();
        SymbolEncoding.IsSymbolFont(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"))).ShouldBeFalse();
        SymbolEncoding.IsSymbolFont(new byte[] { 0, 1, 0, 0, 0, 9 }).ShouldBeFalse();
        SymbolEncoding.IsSymbolFont(SystemFont("wingding.ttf").AsSpan(0, 40)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(RasterizerBackend.FreeType)]
    [InlineData(RasterizerBackend.StbTrueType)]
    [InlineData(RasterizerBackend.Gdi)]
    [InlineData(RasterizerBackend.DirectWrite)]
    public void Generate_RendersTheSameGlyphAtBothCodes(RasterizerBackend backend)
    {
        Assert.SkipUnless(RasterizerFactory.IsRegistered(backend), $"{backend} is not available in this build.");

        // Wingdings 'J' (0x4A / U+F04A) is the smiley; byte 0x80 is a circled 0, read as U+20AC.
        var result = BmFont.Generate(SystemFont("wingding.ttf"), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(new[] { 0x4A, 0xF04A, 0x20AC, 0xF080 }),
            Backend = backend,
        });

        result.FailedCodepoints.ShouldBeEmpty();
        var chars = result.Model.Characters.ToDictionary(c => c.Id);
        chars.Keys.Order().ShouldBe(new[] { 0x4A, 0x20AC, 0xF04A, 0xF080 });
        foreach (var (low, high) in new[] { (0x4A, 0xF04A), (0x20AC, 0xF080) })
        {
            var a = chars[low];
            var b = chars[high];
            a.Width.ShouldBeGreaterThan(0, $"U+{low:X4} on {backend}");
            (a.Width, a.Height, a.XOffset, a.YOffset, a.XAdvance).ShouldBe((b.Width, b.Height, b.XOffset, b.YOffset, b.XAdvance), $"U+{low:X4} on {backend}");
        }
    }
}
