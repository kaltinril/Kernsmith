using System.Globalization;
using System.Text;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Variations;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 171 Wave 2 — CFF2 variable fonts end to end: charstring <c>blend</c> follows the
/// requested instance, advances follow <c>HVAR</c>, Native agrees with FreeType, the default
/// instance is untouched, and the instance reaches synthetic styles, SDF and LCD.
/// </summary>
/// <remarks>
/// The fixture is Source Code Pro's upright variable font: one <c>wght</c> axis, 200..900 with
/// the default at 200 (ExtraLight), so 200 is the default instance. FreeType runs unhinted, as in
/// <see cref="NativeVariableFontTests"/>.
/// </remarks>
public class NativeCff2VariationTests
{
    private const string Sample = "AHOQRSWaegkmosy0258&@$%";

    private const float Size = 48f;

    /// <summary>
    /// Per-glyph SSIM floor against FreeType. The same outline through the two fills scores 0.95+;
    /// the default instance against wght 900 scores far lower, which
    /// <see cref="Comparison_RejectsAnUnvariedGlyph"/> pins.
    /// </summary>
    private const double SsimFloor = 0.95;

    private static readonly Dictionary<string, float> Black = new() { ["wght"] = 900 };

    public static TheoryData<string> Instances => ["wght=200", "wght=400", "wght=650", "wght=900"];

    [Fact]
    public void OutlineDecoders_HandTheVariationStateToTheCffDecoder()
    {
        var face = NativeFontFace.Load(TestFonts.SourceCodeVariableOtfBytes());
        var variations = FontVariations.Load(face);
        variations.ShouldNotBeNull();

        var decoder = OutlineDecoders.ForFace(face, variations).ShouldBeOfType<CffOutlineDecoder>();
        decoder.Variations.ShouldBeSameAs(variations);
    }

    [Theory]
    [MemberData(nameof(Instances))]
    public void SourceCode_MatchesFreeType(string instance)
    {
        var failures = CompareWithFreeType(TestFonts.SourceCodeVariableOtfBytes(), NativeVariableFontTests.Axes(instance));

        failures.ShouldBeEmpty($"Source Code {instance} disagrees with FreeType");
    }

    [Fact]
    public void Comparison_RejectsAnUnvariedGlyph()
    {
        // Guards the comparison: Native's default instance against FreeType's wght 900 must fail it.
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var options = new RasterOptions { Size = Size, Dpi = 72, EnableHinting = false };

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), Black);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);

        var expected = freeType.RasterizeGlyph('H', options)!;
        var actual = native.RasterizeGlyph('H', options)!;

        NativeVariableFontTests.Ssim(expected, actual).ShouldBeLessThan(SsimFloor);
    }

    [Fact]
    public void SetVariationAxes_OnACff2Font_MakesTheGlyphsHeavier()
    {
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var options = new RasterOptions { Size = 32 };

        using var plain = new NativeRasterizer();
        plain.LoadFont(bytes);
        using var varied = new NativeRasterizer();
        varied.LoadFont(bytes);
        varied.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), Black);

        int changed = 0, glyphs = 0;
        for (int c = 33; c < 127; c++)
        {
            var expected = plain.RasterizeGlyph(c, options);
            var actual = varied.RasterizeGlyph(c, options);
            if (expected is null)
                continue;

            actual.ShouldNotBeNull();
            glyphs++;
            if (!actual!.BitmapData.AsSpan().SequenceEqual(expected.BitmapData))
                changed++;
        }

        changed.ShouldBe(glyphs, "every printable ASCII glyph varies with weight");

        long plainInk = plain.RasterizeGlyph('H', options)!.BitmapData.Sum(b => (long)b);
        long blackInk = varied.RasterizeGlyph('H', options)!.BitmapData.Sum(b => (long)b);
        blackInk.ShouldBeGreaterThan(plainInk * 2);
    }

    [Fact]
    public void DefaultCoordinates_AreByteIdenticalToNoAxes()
    {
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var axes = NativeVariableFontTests.FvarAxes(bytes);
        var options = new RasterOptions { Size = 17.5f, Dpi = 72 };

        using var plain = new NativeRasterizer();
        plain.LoadFont(bytes);
        using var explicitDefault = new NativeRasterizer();
        explicitDefault.LoadFont(bytes);
        explicitDefault.SetVariationAxes(axes, axes.ToDictionary(a => a.Tag, a => a.DefaultValue));

        for (int c = 0x20; c < 0x100; c++)
        {
            var a = plain.RasterizeGlyph(c, options);
            var b = explicitDefault.RasterizeGlyph(c, options);
            if (a is null)
            {
                b.ShouldBeNull();
                continue;
            }

            b.ShouldNotBeNull();
            b!.Metrics.ShouldBe(a.Metrics, $"U+{c:X4}");
            b.BitmapData.ShouldBe(a.BitmapData, $"U+{c:X4}");
        }
    }

    [Fact]
    public void ReturningToTheDefaultInstance_RestoresTheDefaultGlyphs()
    {
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var axes = NativeVariableFontTests.FvarAxes(bytes);
        var options = new RasterOptions { Size = 32 };

        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        var initial = native.RasterizeGlyph('g', options)!;

        native.SetVariationAxes(axes, Black);
        var black = native.RasterizeGlyph('g', options)!;
        native.SetVariationAxes(axes, new Dictionary<string, float> { ["wght"] = 200 });
        var restored = native.RasterizeGlyph('g', options)!;

        black.BitmapData.ShouldNotBe(initial.BitmapData);
        restored.BitmapData.ShouldBe(initial.BitmapData);
        restored.Metrics.ShouldBe(initial.Metrics);
    }

    [Fact]
    public void Hvar_AdvancesFollowTheInstance_LikeFreeType()
    {
        // Source Code is monospaced and its own HVAR is all zeros, so swap in one that widens
        // every glyph by 200 units at wght 900. At 1000px (unitsPerEm 1000) a unit is a pixel.
        var bytes = SyntheticFonts.WithTables(
            TestFonts.SourceCodeVariableOtfBytes(), new Dictionary<string, byte[]?> { ["HVAR"] = WideningHvar() });
        var options = new RasterOptions { Size = 1000, Dpi = 72, EnableHinting = false };
        var axes = NativeVariableFontTests.FvarAxes(bytes);

        using var native = new NativeRasterizer();
        native.LoadFont(bytes);
        native.GetGlyphMetrics('H', options)!.Value.Advance.ShouldBe(600);

        native.SetVariationAxes(axes, Black);
        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(axes, Black);

        foreach (char c in "Hi.")
        {
            int expected = freeType.GetGlyphMetrics(c, options)!.Value.Advance;
            expected.ShouldBe(800, $"FreeType '{c}'");
            native.GetGlyphMetrics(c, options)!.Value.Advance.ShouldBe(expected, $"'{c}' measured");
            native.RasterizeGlyph(c, options)!.Metrics.Advance.ShouldBe(expected, $"'{c}' rendered");
        }
    }

    [Fact]
    public void Generate_WithVariationAxes_OnACff2Font_FollowsTheInstance()
    {
        // Through the public pipeline, which forwards VariationAxes because Native advertises
        // SupportsVariableFonts.
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        FontGeneratorOptions Options(RasterizerBackend backend, Dictionary<string, float>? axes) => new()
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = backend,
            VariationAxes = axes,
        };

        var varied = BmFont.Generate(bytes, Options(RasterizerBackend.Native, Black));
        var plain = BmFont.Generate(bytes, Options(RasterizerBackend.Native, null));
        var freeType = BmFont.Generate(bytes, Options(RasterizerBackend.FreeType, Black));

        varied.GetPngData()[0].ShouldNotBe(plain.GetPngData()[0], "the varied atlas is the default one");

        var nativeFnt = BmFontReader.ReadText(varied.FntText);
        var freeTypeFnt = BmFontReader.ReadText(freeType.FntText);
        nativeFnt.Common.Base.ShouldBe(freeTypeFnt.Common.Base);
        nativeFnt.Common.LineHeight.ShouldBe(freeTypeFnt.Common.LineHeight);
        foreach (var expected in freeTypeFnt.Characters)
        {
            var actual = nativeFnt.Characters.First(c => c.Id == expected.Id);
            actual.XAdvance.ShouldBe(expected.XAdvance, $"U+{expected.Id:X4} xadvance");
        }
    }

    public static TheoryData<string> Styles => ["bold", "italic", "bold+italic", "sdf", "lcd"];

    [Theory]
    [MemberData(nameof(Styles))]
    public void VariedGlyph_ReachesStylesAndRenderModes(string style)
    {
        // Each mode works from the decoded outline, so the instance must show through all of them:
        // the varied glyph differs from the default one under the same options, and carries more ink.
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var options = new RasterOptions
        {
            Size = 32,
            Bold = style.Contains("bold", StringComparison.Ordinal),
            Italic = style.Contains("italic", StringComparison.Ordinal),
            Sdf = style == "sdf",
            AntiAlias = style == "lcd" ? AntiAliasMode.Lcd : AntiAliasMode.Grayscale,
        };

        using var plain = new NativeRasterizer();
        plain.LoadFont(bytes);
        using var varied = new NativeRasterizer();
        varied.LoadFont(bytes);
        varied.SetVariationAxes(NativeVariableFontTests.FvarAxes(bytes), Black);

        var expected = plain.RasterizeGlyph('H', options)!;
        var actual = varied.RasterizeGlyph('H', options)!;

        actual.Format.ShouldBe(expected.Format);
        actual.BitmapData.ShouldNotBe(expected.BitmapData);
        actual.BitmapData.Sum(b => (long)b).ShouldBeGreaterThan(expected.BitmapData.Sum(b => (long)b));
        varied.GetGlyphMetrics('H', options).ShouldBe(actual.Metrics, "Measure agrees with Render");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Renders <see cref="Sample"/> through both backends at one instance and lists every glyph
    /// whose advance or ink extent differs by more than a pixel, or whose bitmap scores under
    /// <see cref="SsimFloor"/>.
    /// </summary>
    private static string CompareWithFreeType(byte[] bytes, Dictionary<string, float> axes)
    {
        var options = new RasterOptions { Size = Size, Dpi = 72, EnableHinting = false };
        var fvar = NativeVariableFontTests.FvarAxes(bytes);

        using IRasterizer freeType = new FreeTypeRasterizer();
        freeType.LoadFont(bytes);
        freeType.SetVariationAxes(fvar, axes);
        using IRasterizer native = new NativeRasterizer();
        native.LoadFont(bytes);
        native.SetVariationAxes(fvar, axes);

        var failures = new StringBuilder();
        foreach (char c in Sample)
        {
            var expected = freeType.RasterizeGlyph(c, options)!;
            var actual = native.RasterizeGlyph(c, options)!;

            Check(failures, c, "advance", expected.Metrics.Advance, actual.Metrics.Advance);

            var expectedInk = NativeVariableFontTests.InkBounds(expected)!.Value;
            var actualInk = NativeVariableFontTests.InkBounds(actual)!.Value;
            Check(failures, c, "ink left", expectedInk.Left, actualInk.Left);
            Check(failures, c, "ink top", expectedInk.Top, actualInk.Top);
            Check(failures, c, "ink right", expectedInk.Right, actualInk.Right);
            Check(failures, c, "ink bottom", expectedInk.Bottom, actualInk.Bottom);

            double ssim = NativeVariableFontTests.Ssim(expected, actual);
            if (ssim < SsimFloor)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}': SSIM {ssim:F3}\n");
        }

        return failures.ToString();
    }

    private static void Check(StringBuilder failures, char c, string field, int expected, int actual)
    {
        if (Math.Abs(expected - actual) > 1)
            failures.Append(CultureInfo.InvariantCulture, $"'{c}' {field}: FreeType {expected}, native {actual}\n");
    }

    /// <summary>
    /// An <c>HVAR</c> whose advance map sends every glyph (the last entry repeats) to one delta
    /// set of +200 units in a single region peaking at wght +1.
    /// </summary>
    private static byte[] WideningHvar()
    {
        byte[] regions = Be.Concat(Be.U16(1), Be.U16(1), Be.F2Dot14(0), Be.F2Dot14(1), Be.F2Dot14(1));
        byte[] data = Be.Concat(Be.U16(1), Be.U16(1), Be.U16(1), Be.U16(0), Be.I16(200));
        const int StoreHeader = 2 + 4 + 2 + 4;
        byte[] store = Be.Concat(
            Be.U16(1), Be.U32(StoreHeader), Be.U16(1), Be.U32((uint)(StoreHeader + regions.Length)),
            regions, data);

        const int Header = 20;
        byte[] advanceMap = [0, 0, 0, 1, 0];   // format 0, entryFormat 0 (1-byte entries, 1 inner bit), mapCount 1, entry (0, 0)
        return Be.Concat(
            Be.U16(1), Be.U16(0), Be.U32(Header), Be.U32((uint)(Header + store.Length)), Be.U32(0), Be.U32(0),
            store, advanceMap);
    }
}
