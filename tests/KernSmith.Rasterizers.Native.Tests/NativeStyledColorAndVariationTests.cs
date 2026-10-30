using System.Globalization;
using System.Text;
using KernSmith.Font.Models;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Synthetic bold and italic (Phase 167) combined with the other outline-level features: every
/// <c>COLR</c> layer of a color glyph (Phase 172) is styled, core leaves Native's RGBA output to
/// it instead of running the bitmap fallbacks, and a variable-font instance (Phase 171) is styled
/// after its variation is applied. Distance fields (Phases 169-170) are built from the styled
/// outline.
/// </summary>
public class NativeStyledColorAndVariationTests
{
    // ---- Color layers ----------------------------------------------------------------------

    [Theory]
    [InlineData('A')]
    [InlineData('O')]
    [InlineData('g')]
    public void BoldColorGlyph_EmboldensItsLayers(char character)
    {
        // The base glyph's own outline is never drawn for a COLR glyph — only its layers — so a
        // wider, heavier RGBA glyph can only come from emboldened layers.
        using var native = Load(TestFonts.BungeeColorBytes());
        var regular = native.RasterizeGlyph(character, ColorOptions(bold: false, italic: false))!;
        var bold = native.RasterizeGlyph(character, ColorOptions(bold: true, italic: false))!;
        var plainBold = native.RasterizeGlyph(character, ColorOptions(bold: true, italic: false) with { ColorFont = false })!;

        bold.Format.ShouldBe(PixelFormat.Rgba32);
        bold.Width.ShouldBeGreaterThan(regular.Width, "the color layers were not emboldened");
        AlphaSum(bold).ShouldBeGreaterThan(AlphaSum(regular));
        DistinctColors(bold).ShouldBeGreaterThan(1, "the emboldened glyph lost its layer colors");
        bold.Metrics.Advance.ShouldBe(plainBold.Metrics.Advance);
        bold.Metrics.Advance.ShouldBeGreaterThan(regular.Metrics.Advance);
    }

    [Fact]
    public void BoldColrV1Glyph_EmboldensItsGlyphClips()
    {
        // ReemKufiInk is COLR version 1: its PaintGlyph clips go through the same transforms as
        // version 0 layers, so each color glyph widens, gains ink and keeps its palette colors.
        // One-ar ('١') is left out: its clip box fixes the canvas whatever the outline does.
        const string Sample = "٤؟ءَّ۵";
        using var native = Load(TestFonts.ReemKufiInkBytes());

        int colored = 0;
        foreach (char c in Sample)
        {
            var regular = native.RasterizeGlyph(c, ColorOptions(bold: false, italic: false))!;
            var bold = native.RasterizeGlyph(c, ColorOptions(bold: true, italic: false))!;
            var plainBold = native.RasterizeGlyph(c, ColorOptions(bold: true, italic: false) with { ColorFont = false })!;

            bold.Format.ShouldBe(PixelFormat.Rgba32, $"U+{(int)c:X4}");
            bold.Width.ShouldBeGreaterThan(regular.Width, $"U+{(int)c:X4}: the version 1 glyph clips were not emboldened");
            AlphaSum(bold).ShouldBeGreaterThan(AlphaSum(regular), $"U+{(int)c:X4}");
            bold.Metrics.Advance.ShouldBe(plainBold.Metrics.Advance, $"U+{(int)c:X4}");
            native.GetGlyphMetrics(c, ColorOptions(bold: true, italic: false)).ShouldBe(bold.Metrics, $"U+{(int)c:X4}");
            colored += NonBlackOpaquePixels(bold);
        }

        colored.ShouldBeGreaterThan(100, "the emboldened version 1 glyphs lost their palette colors");
    }

    [Fact]
    public void ItalicColorGlyph_ShearsItsLayers()
    {
        // A 12° shear moves the top of a 48px cap well to the right of its bottom: the ink box
        // widens while the advance stays.
        using var native = Load(TestFonts.BungeeColorBytes());
        var regular = native.RasterizeGlyph('H', ColorOptions(bold: false, italic: false))!;
        var italic = native.RasterizeGlyph('H', ColorOptions(bold: false, italic: true))!;

        italic.Format.ShouldBe(PixelFormat.Rgba32);
        italic.Width.ShouldBeGreaterThan(regular.Width + 4, "the color layers were not slanted");
        italic.Metrics.Advance.ShouldBe(regular.Metrics.Advance);
        DistinctColors(italic).ShouldBeGreaterThan(1);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Generate_StyledColorFont_OnNative_SkipsTheBitmapPostProcessors(bool bold, bool italic)
    {
        // Native styles each color layer itself, so core must not dilate or shear its RGBA
        // output again: supplying the fallback post-processor changes nothing.
        IGlyphPostProcessor fallback = bold ? new BoldPostProcessor(2) : new ItalicPostProcessor();

        var withFallback = BmFont.Generate(TestFonts.BungeeColorBytes(), GenerateOptions(bold, italic, [fallback]));
        var nativeOnly = BmFont.Generate(TestFonts.BungeeColorBytes(), GenerateOptions(bold, italic, null));
        var unstyled = BmFont.Generate(TestFonts.BungeeColorBytes(), GenerateOptions(false, false, null));

        withFallback.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);
        withFallback.FntText.ShouldBe(nativeOnly.FntText);
        withFallback.Pages[0].PixelData.ShouldBe(nativeOnly.Pages[0].PixelData);
        nativeOnly.FntText.ShouldNotBe(unstyled.FntText, "Native did not style the color glyphs");
    }

    // ---- Variable instances ----------------------------------------------------------------

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StyledVariableInstance_MatchesFreeType(bool bold, bool italic)
    {
        // RobotoFlex at wght 700, then synthetic styling on top — FreeType applies its variation
        // and its embolden/oblique the same way round.
        const string Sample = "AHOQRSWaegkmos0258&";
        var axes = new Dictionary<string, float> { ["wght"] = 700f };
        var options = new RasterOptions { Size = 48, Dpi = 72, EnableHinting = false, Bold = bold, Italic = italic };
        var plainOptions = options with { Bold = false, Italic = false };

        using var freeType = LoadVariable(new FreeTypeRasterizer(), axes);
        using var native = LoadVariable(new NativeRasterizer(), axes);
        using var nativeDefault = LoadVariable(new NativeRasterizer(), new Dictionary<string, float>());

        var failures = new StringBuilder();
        foreach (char c in Sample)
        {
            var expected = freeType.RasterizeGlyph(c, options)!;
            var actual = native.RasterizeGlyph(c, options)!;
            var plain = native.RasterizeGlyph(c, plainOptions)!;

            if (Math.Abs(expected.Metrics.Advance - actual.Metrics.Advance) > 1)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}' advance: FreeType {expected.Metrics.Advance}, Native {actual.Metrics.Advance}\n");

            double styled = NativeSyntheticStyleTests.SsimOnSharedCanvas(actual, expected);
            double unstyled = NativeSyntheticStyleTests.SsimOnSharedCanvas(plain, expected);
            if (styled < 0.9 || styled <= unstyled)
                failures.Append(CultureInfo.InvariantCulture, $"'{c}': styled {styled:F4}, unstyled {unstyled:F4}\n");

            // The instance still applies under the styling.
            var atDefault = nativeDefault.RasterizeGlyph(c, options)!;
            if (atDefault.BitmapData.AsSpan().SequenceEqual(actual.BitmapData))
                failures.Append(CultureInfo.InvariantCulture, $"'{c}': wght 700 rendered the default instance\n");
        }

        failures.ToString().ShouldBeEmpty($"RobotoFlex wght=700 bold={bold} italic={italic}");
    }

    // ---- Distance fields -------------------------------------------------------------------

    [Theory]
    [InlineData('H')]
    [InlineData('O')]
    [InlineData('g')]
    [InlineData('m')]
    public void BoldSdf_FieldIsBuiltFromTheEmboldenedOutline(char character)
    {
        // SDF (Phase 169) measures the outline the transforms produced: an emboldened glyph's
        // field covers a wider box and its inside (>= 128) region grows, and metrics still
        // describe exactly the bitmap rendered.
        using var native = Load(TestFonts.RobotoRegularBytes());
        var plainOptions = new RasterOptions { Size = 32, Dpi = 72, Sdf = true, SdfSpread = 8 };
        var boldOptions = plainOptions with { Bold = true };

        var plain = native.RasterizeGlyph(character, plainOptions)!;
        var bold = native.RasterizeGlyph(character, boldOptions)!;

        bold.Format.ShouldBe(PixelFormat.Grayscale8);
        bold.Width.ShouldBeGreaterThan(plain.Width, "the distance field ignored synthetic bold");
        InsideCount(bold, step: 1).ShouldBeGreaterThan(InsideCount(plain, step: 1));
        bold.Metrics.Advance.ShouldBeGreaterThan(plain.Metrics.Advance);
        native.GetGlyphMetrics(character, boldOptions).ShouldBe(bold.Metrics);
    }

    [Theory]
    [InlineData(SdfMode.Msdf)]
    [InlineData(SdfMode.Mtsdf)]
    public void ItalicMsdf_FieldIsBuiltFromTheShearedOutline(SdfMode mode)
    {
        // A 12° shear widens a 32px cap's box by several pixels while the advance stays.
        using var native = Load(TestFonts.RobotoRegularBytes());
        var plainOptions = new RasterOptions { Size = 32, Dpi = 72, SdfMode = mode, SdfSpread = 8 };
        var italicOptions = plainOptions with { Italic = true };

        var plain = native.RasterizeGlyph('H', plainOptions)!;
        var italic = native.RasterizeGlyph('H', italicOptions)!;

        italic.Format.ShouldBe(PixelFormat.Rgba32);
        italic.Width.ShouldBeGreaterThanOrEqualTo(plain.Width + 4, "the multi-channel field ignored synthetic italic");
        italic.Metrics.Advance.ShouldBe(plain.Metrics.Advance);
        InsideCount(italic, step: 4).ShouldBeGreaterThan(0);
        native.GetGlyphMetrics('H', italicOptions).ShouldBe(italic.Metrics);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    /// <summary>Texels whose (first-channel) distance encodes inside the outline.</summary>
    private static int InsideCount(RasterizedGlyph glyph, int step)
    {
        int count = 0;
        for (int i = 0; i < glyph.BitmapData.Length; i += step)
        {
            if (glyph.BitmapData[i] >= 128)
                count++;
        }

        return count;
    }

    private static RasterOptions ColorOptions(bool bold, bool italic) =>
        new() { Size = 48, Dpi = 72, ColorFont = true, Bold = bold, Italic = italic };

    private static FontGeneratorOptions GenerateOptions(bool bold, bool italic, List<IGlyphPostProcessor>? postProcessors) => new()
    {
        Size = 32,
        Characters = CharacterSet.FromChars("ABCabc0123&?"),
        Backend = RasterizerBackend.Native,
        ColorFont = true,
        Bold = bold,
        Italic = italic,
        PostProcessors = postProcessors
    };

    private static NativeRasterizer Load(byte[] font)
    {
        var native = new NativeRasterizer();
        native.LoadFont(font);
        return native;
    }

    private static IRasterizer LoadVariable(IRasterizer rasterizer, Dictionary<string, float> axes)
    {
        var bytes = TestFonts.RobotoFlexBytes();
        rasterizer.LoadFont(bytes);
        rasterizer.SetVariationAxes(BmFont.ReadFontInfo(bytes).VariationAxes!, axes);
        return rasterizer;
    }

    private static long AlphaSum(RasterizedGlyph glyph)
    {
        long sum = 0;
        for (int i = 3; i < glyph.BitmapData.Length; i += 4)
            sum += glyph.BitmapData[i];

        return sum;
    }

    private static int NonBlackOpaquePixels(RasterizedGlyph glyph)
    {
        int count = 0;
        for (int i = 0; i < glyph.BitmapData.Length; i += 4)
        {
            if (glyph.BitmapData[i + 3] == 255 && (glyph.BitmapData[i] | glyph.BitmapData[i + 1] | glyph.BitmapData[i + 2]) != 0)
                count++;
        }

        return count;
    }

    private static int DistinctColors(RasterizedGlyph glyph)
    {
        var colors = new HashSet<int>();
        for (int i = 0; i < glyph.BitmapData.Length; i += 4)
        {
            if (glyph.BitmapData[i + 3] == 255)
                colors.Add((glyph.BitmapData[i] << 16) | (glyph.BitmapData[i + 1] << 8) | glyph.BitmapData[i + 2]);
        }

        return colors.Count;
    }
}
