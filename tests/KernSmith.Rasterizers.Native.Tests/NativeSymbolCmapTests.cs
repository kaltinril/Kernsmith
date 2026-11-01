using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// A font whose only <c>cmap</c> subtable is the Windows symbol one (3,0) — Wingdings, Symbol,
/// Webdings — renders each glyph at its U+F0xx code and at that byte's Windows-1252 character, as
/// core and BMFont's SYMBOL charset expose them.
/// </summary>
public sealed class NativeSymbolCmapTests
{
    private static byte[] SystemFont(string file)
    {
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);
        Assert.SkipUnless(File.Exists(path), $"{file} is not installed on this machine.");
        return File.ReadAllBytes(path);
    }

    [Theory]
    [InlineData("wingding.ttf", 0x4A)]
    [InlineData("wingding.ttf", 0x20AC)] // byte 0x80, read as Windows-1252
    [InlineData("symbol.ttf", 0x61)]
    [InlineData("webdings.ttf", 0x61)]
    public void SymbolGlyph_RendersTheSameAtBothCodes(string file, int low)
    {
        using var native = new NativeRasterizer();
        native.LoadFont(SystemFont(file));
        var options = new KernSmith.Rasterizer.RasterOptions { Size = 32 };

        var a = native.RasterizeGlyph(low, options);
        var b = native.RasterizeGlyph(KernSmith.Font.SymbolEncoding.ToSymbolCode(low)!.Value, options);

        a.ShouldNotBeNull();
        b.ShouldNotBeNull();
        a.Width.ShouldBeGreaterThan(0);
        (a.Width, a.Height, a.Metrics).ShouldBe((b.Width, b.Height, b.Metrics));
        a.BitmapData.ShouldBe(b.BitmapData);
    }

    [Fact]
    public void Generate_ReportsNoFailures()
    {
        var result = BmFont.Generate(SystemFont("wingding.ttf"), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(new[] { 0x41, 0x4A, 0xF041, 0xF04A }),
            Rasterizer = new NativeRasterizer(),
        });

        result.FailedCodepoints.ShouldBeEmpty();
        result.Model.Characters.Count.ShouldBe(4);
    }
}
