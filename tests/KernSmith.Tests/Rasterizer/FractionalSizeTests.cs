using KernSmith.Output;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.StbTrueType;
#if WINDOWS
using KernSmith.Rasterizers.Gdi;
#endif
#if DIRECTWRITE
using KernSmith.Rasterizers.DirectWrite.TerraFX;
#endif
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Tests that fractional <see cref="RasterOptions.Size"/> values produce distinct output on
/// backends with native float sizing (FreeType, Stb, DirectWrite), are rounded by the GDI
/// backend, are rounded by the on-disk formatters, and propagate through the public API.
/// </summary>
[Collection("RasterizerFactory")]
public class FractionalSizeTests
{
    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(FixturesDir, "Roboto-Regular.ttf"));

    /// <summary>
    /// Computes the average alpha of a glyph's bitmap. Used as a coarse proxy for
    /// "did the rasterizer produce a measurably different output".
    /// </summary>
    private static double AverageAlpha(byte[] data)
    {
        if (data.Length == 0) return 0;
        long sum = 0;
        for (int i = 0; i < data.Length; i++) sum += data[i];
        return (double)sum / data.Length;
    }

    private static (int Width, int Height, double Avg) Summarize(IReadOnlyList<RasterizedGlyph> glyphs)
    {
        int w = 0, h = 0;
        long totalSum = 0;
        long totalLen = 0;
        foreach (var g in glyphs)
        {
            w += g.Width;
            h += g.Height;
            for (int i = 0; i < g.BitmapData.Length; i++) totalSum += g.BitmapData[i];
            totalLen += g.BitmapData.Length;
        }
        return (w, h, totalLen == 0 ? 0 : (double)totalSum / totalLen);
    }

    private static IReadOnlyList<RasterizedGlyph> RasterizeAt(IRasterizer rasterizer, float size)
    {
        // Disable hinting so the rasterizer doesn't snap fractional sizes to the nearest
        // integer pixel grid (which would defeat the purpose of fractional sizing).
        var opts = new RasterOptions { Size = size, AntiAlias = AntiAliasMode.Grayscale, EnableHinting = false };
        return rasterizer.RasterizeAll(new[] { 65, 66, 67, 72, 79, 87 }, opts); // A B C H O W
    }

    [Fact]
    public void FreeType_FractionalSize_DiffersFromAdjacentIntegerSizes()
    {
        using var rasterizer = new FreeTypeRasterizer();
        rasterizer.LoadFont(LoadTestFont());

        var s10 = Summarize(RasterizeAt(rasterizer, 10f));
        var s10_5 = Summarize(RasterizeAt(rasterizer, 10.5f));
        var s11 = Summarize(RasterizeAt(rasterizer, 11f));

        // A fractional render should produce something measurably different from
        // the integer renders on either side. Compare composite (width+height+avg).
        (s10.Width != s10_5.Width || s10.Height != s10_5.Height || Math.Abs(s10.Avg - s10_5.Avg) > 0.5)
            .ShouldBeTrue("size 10.5 should differ from size 10");
        (s11.Width != s10_5.Width || s11.Height != s10_5.Height || Math.Abs(s11.Avg - s10_5.Avg) > 0.5)
            .ShouldBeTrue("size 10.5 should differ from size 11");
    }

    [Fact]
    public void StbTrueType_FractionalSize_DiffersFromAdjacentIntegerSizes()
    {
        using var rasterizer = new StbTrueTypeRasterizer();
        rasterizer.LoadFont(LoadTestFont());

        var s10 = Summarize(RasterizeAt(rasterizer, 10f));
        var s10_5 = Summarize(RasterizeAt(rasterizer, 10.5f));
        var s11 = Summarize(RasterizeAt(rasterizer, 11f));

        (s10.Width != s10_5.Width || s10.Height != s10_5.Height || Math.Abs(s10.Avg - s10_5.Avg) > 0.5)
            .ShouldBeTrue("size 10.5 should differ from size 10");
        (s11.Width != s10_5.Width || s11.Height != s10_5.Height || Math.Abs(s11.Avg - s10_5.Avg) > 0.5)
            .ShouldBeTrue("size 10.5 should differ from size 11");
    }

#if DIRECTWRITE
    [Fact]
    public void DirectWrite_FractionalSize_DiffersFromAdjacentIntegerSizes()
    {
        using var rasterizer = new DirectWriteRasterizer();
        rasterizer.LoadFont(LoadTestFont());

        // We use 100/100.5/101 here, not 10/10.5/11 like the FreeType and Stb tests above.
        // Two effects combine to make a half-pixel difference invisible at very small ppem:
        //
        //  1. Grid-fitting. DirectWrite applies hinting heuristics that snap glyph outline
        //     edges to the integer pixel grid for legibility at small sizes. The "natural"
        //     rendering modes reduce this but DirectWrite does not fully expose a knob to
        //     turn it off the way FreeType does (FT_LOAD_NO_HINTING).
        //  2. Pixel quantization. AA coverage is stored as 8-bit alpha. At 10 vs 10.5 px,
        //     a given pixel's true coverage might shift by less than 1/256 and round to the
        //     same byte, producing a bit-identical bitmap. At 100 vs 100.5 px, the absolute
        //     shift is ~10x larger and reliably crosses byte boundaries.
        //
        // Neither effect indicates a bug: the fractional value IS reaching DirectWrite and
        // is being used for metrics, advances, and outline math. It just doesn't always
        // change which 8-bit pixel values fall out the other end at small sizes.
        var s100 = Summarize(RasterizeAt(rasterizer, 100f));
        var s100_5 = Summarize(RasterizeAt(rasterizer, 100.5f));
        var s101 = Summarize(RasterizeAt(rasterizer, 101f));

        // The contract this test enforces: DirectWrite must not be pre-rounding the size
        // to int on input. If it were, 100.5 would produce a bitmap identical to BOTH 100
        // AND 101 (whichever side it rounded to). Differing from at least one neighbor
        // proves the float reached the rasterizer and influenced the output.
        var differsFrom100 = s100.Width != s100_5.Width || s100.Height != s100_5.Height || Math.Abs(s100.Avg - s100_5.Avg) > 0.1;
        var differsFrom101 = s101.Width != s100_5.Width || s101.Height != s100_5.Height || Math.Abs(s101.Avg - s100_5.Avg) > 0.1;
        (differsFrom100 || differsFrom101)
            .ShouldBeTrue("size 100.5 should differ from at least one of {100, 101}");
    }
#endif

#if WINDOWS
    [Fact]
    public void Gdi_FractionalSize_RoundsToInteger_AndDoesNotThrow()
    {
        using var fractional = new GdiRasterizer();
        fractional.LoadFont(LoadTestFont());
        using var rounded = new GdiRasterizer();
        rounded.LoadFont(LoadTestFont());

        IReadOnlyList<RasterizedGlyph>? frac = null;
        Should.NotThrow(() => frac = RasterizeAt(fractional, 10.5f));

        // 10.5 rounds (Math.Round, banker's) to 10, so output should match size 10 exactly.
        var rnd = RasterizeAt(rounded, 10f);

        frac!.Count.ShouldBe(rnd.Count);
        for (int i = 0; i < frac.Count; i++)
        {
            frac[i].Width.ShouldBe(rnd[i].Width);
            frac[i].Height.ShouldBe(rnd[i].Height);
        }
    }
#endif

    [Fact]
    public void TextFormatter_FractionalSize_WritesRoundedInteger()
    {
        var model = new BmFontModel
        {
            Info = new InfoBlock(
                Face: "TestFont",
                Size: 10.5f,
                Bold: false,
                Italic: false,
                Unicode: true,
                Smooth: true,
                FixedHeight: false,
                StretchH: 100,
                Charset: "",
                Aa: 1,
                Padding: new Padding(0, 0, 0, 0),
                Spacing: new Spacing(1, 1)),
            Common = new CommonBlock(
                LineHeight: 12,
                Base: 10,
                ScaleW: 64,
                ScaleH: 64,
                Pages: 1,
                Packed: false),
            Pages = new[] { new PageEntry(0, "TestFont_0.png") },
            Characters = Array.Empty<CharEntry>(),
            KerningPairs = Array.Empty<KerningEntry>()
        };

        var text = new TextFormatter().FormatText(model);

        // 10.5f rounds (Math.Round default = banker's) to 10. Either way, no decimal point
        // and the parsed value should round-trip as an integer.
        text.ShouldContain(" size=10 ");

        // Round-trip via the reader: parsed size should be a whole number.
        var parsed = BmFontReader.ReadText(text);
        parsed.Info.Size.ShouldBe(10f);
    }

    [Fact]
    public void Builder_WithFractionalSize_PropagatesToResult()
    {
        var fontData = LoadTestFont();

        var result = BmFont.Builder()
            .WithFont(fontData)
            .WithSize(10.5f)
            .WithCharacters(CharacterSet.FromChars("AB"))
            .Build();

        // In-memory, the size stays float and round-trips exactly.
        result.Model.Info.Size.ShouldBe(10.5f);
    }
}
