using System.Buffers.Binary;
using System.Text;
using FreeTypeSharp;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// With <c>FT_LOAD_COLOR</c>, FreeType loads a glyph that has an <c>SVG </c> document as an SVG
/// glyph, which it can only render through hooks KernSmith does not install. Reem Kufi Ink's 167
/// Latin glyphs are colored only by SVG documents (its COLR version 1 glyphs are the Arabic ones),
/// so they must render as their plain outline instead of failing the font.
/// </summary>
[Collection("RasterizerFactory")]
public class FreeTypeSvgGlyphTests
{
    /// <summary>Latin letters: Reem Kufi Ink's glyphs with an SVG document and no COLR entry.</summary>
    private static readonly int[] SvgCodepoints = ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'L'];

    private static byte[] Fixture(params string[] path) =>
        File.ReadAllBytes(Path.Combine([AppContext.BaseDirectory, "Fixtures", .. path]));

    private static byte[] Reem() => Fixture("Color", "ReemKufiInk-Regular.ttf");

    private static RasterOptions Color(AntiAliasMode antiAlias = AntiAliasMode.Grayscale) =>
        new() { Size = 32, Dpi = 72, ColorFont = true, AntiAlias = antiAlias };

    private static FreeTypeRasterizer Load(byte[] font)
    {
        var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(font);
        return rasterizer;
    }

    [Theory]
    [InlineData(AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.Light)]
    [InlineData(AntiAliasMode.Lcd)]
    public void EveryGlyph_RendersWithColorOn(AntiAliasMode antiAlias)
    {
        var font = Reem();
        using var rasterizer = Load(font);
        var options = Color(antiAlias);

        var failed = new List<string>();
        foreach (var codepoint in BmFont.ReadFontInfo(font).AvailableCodepoints)
        {
            try
            {
                rasterizer.RasterizeGlyph(codepoint, options);
            }
            catch (FreeTypeException ex)
            {
                failed.Add($"U+{codepoint:X4}: {ex.Message}");
            }
        }

        failed.ShouldBeEmpty($"{failed.Count} glyphs threw; first: {failed.FirstOrDefault()}");
    }

    [Theory]
    [InlineData(AntiAliasMode.None)]
    [InlineData(AntiAliasMode.Grayscale)]
    [InlineData(AntiAliasMode.Light)]
    [InlineData(AntiAliasMode.Lcd)]
    public void SvgGlyph_RendersAsItsPlainOutline(AntiAliasMode antiAlias)
    {
        using var rasterizer = Load(Reem());
        var color = Color(antiAlias);
        var plain = color with { ColorFont = false };

        foreach (var codepoint in SvgCodepoints)
        {
            var withColor = rasterizer.RasterizeGlyph(codepoint, color).ShouldNotBeNull();
            var withoutColor = rasterizer.RasterizeGlyph(codepoint, plain).ShouldNotBeNull();

            withColor.Format.ShouldBe(withoutColor.Format);
            withColor.Metrics.ShouldBe(withoutColor.Metrics);
            withColor.BitmapData.ShouldBe(withoutColor.BitmapData);
            withColor.BitmapData.ShouldContain(b => b != 0);
        }
    }

    [Fact]
    public void SvgGlyph_MeasuresAsItRenders()
    {
        using var rasterizer = Load(Reem());
        var options = Color();

        foreach (var codepoint in SvgCodepoints)
        {
            var glyph = rasterizer.RasterizeGlyph(codepoint, options).ShouldNotBeNull();
            ((IRasterizer)rasterizer).GetGlyphMetrics(codepoint, options).ShouldBe(glyph.Metrics);
        }
    }

    [Fact]
    public void SvgGlyph_ReportsItsPlainFractionalMetrics()
    {
        using var rasterizer = Load(Reem());
        var color = Color() with { FractionalMetrics = true };
        var plain = color with { ColorFont = false };

        foreach (var codepoint in SvgCodepoints)
        {
            rasterizer.RasterizeGlyph(codepoint, color).ShouldNotBeNull().Fractional
                .ShouldBe(rasterizer.RasterizeGlyph(codepoint, plain).ShouldNotBeNull().Fractional);
        }
    }

    [Fact]
    public void Generate_FaceWithSvgGlyphs_ProducesTheFont()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars(SvgCodepoints),
            Backend = RasterizerBackend.FreeType,
            ColorFont = true,
        };

        var result = BmFont.Generate(Reem(), options);

        result.FailedCodepoints.ShouldBeEmpty();
        result.Model.Characters.Count.ShouldBe(SvgCodepoints.Length);
        result.Pages.Sum(p => p.PixelData.Count(b => b != 0)).ShouldBeGreaterThan(0);
        BmFont.QueryAtlasSize(Reem(), options).GlyphCount.ShouldBe(SvgCodepoints.Length);
    }

    [Fact]
    public void ColrVersion0Glyph_StaysInColor()
    {
        using var rasterizer = Load(Fixture("Color", "BungeeColor-Regular.ttf"));

        var glyph = rasterizer.RasterizeGlyph('A', Color()).ShouldNotBeNull();

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
    }

    [Fact]
    public void ColorBitmapGlyph_StaysInColor()
    {
        using var rasterizer = Load(Fixture("NotoColorEmoji.ttf"));

        var glyph = rasterizer.RasterizeGlyph(0x1F600, Color()).ShouldNotBeNull();

        glyph.Format.ShouldBe(PixelFormat.Rgba32);
    }

    [Fact]
    public void MalformedGlyph_StillThrowsWithColorOn()
    {
        using var rasterizer = Load(WithEveryGlyphCorrupted(Fixture("Roboto-Regular.ttf")));

        Should.Throw<FreeTypeException>(() => rasterizer.RasterizeGlyph('A', Color()));
    }

    /// <summary>Gives every glyph an impossible contour count, which FreeType refuses to load.</summary>
    private static byte[] WithEveryGlyphCorrupted(byte[] font)
    {
        (int Offset, int Length) Table(string tag)
        {
            var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (var i = 0; i < count; i++)
            {
                var record = 12 + (i * 16);
                if (Encoding.ASCII.GetString(font, record, 4) == tag)
                    return (BinaryPrimitives.ReadInt32BigEndian(font.AsSpan(record + 8)),
                        BinaryPrimitives.ReadInt32BigEndian(font.AsSpan(record + 12)));
            }

            throw new InvalidOperationException($"No {tag} table.");
        }

        var head = Table("head").Offset;
        var (loca, locaLength) = Table("loca");
        var glyf = Table("glyf").Offset;
        var longOffsets = BinaryPrimitives.ReadInt16BigEndian(font.AsSpan(head + 50)) != 0;
        var entries = (locaLength / (longOffsets ? 4 : 2)) - 1;

        int Location(int glyph) => longOffsets
            ? BinaryPrimitives.ReadInt32BigEndian(font.AsSpan(loca + (glyph * 4)))
            : BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(loca + (glyph * 2))) * 2;

        for (var glyph = 0; glyph < entries; glyph++)
        {
            if (Location(glyph + 1) > Location(glyph))
                BinaryPrimitives.WriteInt16BigEndian(font.AsSpan(glyf + Location(glyph)), 0x7FFF);
        }

        return font;
    }
}
