using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Raster;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 173 — the public LCD options (<see cref="LcdFilter"/>, <see cref="LcdSubpixelOrder"/>)
/// reach Native's LCD renderer, and LCD output under super sampling is rendered at the final
/// size instead of having its subpixel pattern box-filtered down.
/// </summary>
public class LcdOptionsTests
{
    private const string Sample = "ABHIOSWaegilmosw0158.,!?@&";

    public static TheoryData<LcdFilter, LcdSubpixelOrder> Combinations()
    {
        var data = new TheoryData<LcdFilter, LcdSubpixelOrder>();
        foreach (var filter in Enum.GetValues<LcdFilter>())
        {
            foreach (var order in Enum.GetValues<LcdSubpixelOrder>())
                data.Add(filter, order);
        }

        return data;
    }

    private static RasterOptions Lcd(float size, LcdFilter filter = LcdFilter.Default, LcdSubpixelOrder order = LcdSubpixelOrder.Rgb) =>
        new() { Size = size, AntiAlias = AntiAliasMode.Lcd, LcdFilter = filter, LcdSubpixelOrder = order };

    private static LcdFilterKernel ExpectedKernel(LcdFilter filter) => filter switch
    {
        LcdFilter.Default or LcdFilter.Light => LcdFilterKernel.PixelBox,
        LcdFilter.Smooth => LcdFilterKernel.Triangle,
        LcdFilter.None => LcdFilterKernel.Identity,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    // ---- Dispatch -----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Dispatch_BuildsTheRendererTheOptionsAskFor(LcdFilter filter, LcdSubpixelOrder order)
    {
        var renderer = GlyphRenderers.Select(Lcd(16, filter, order)).ShouldBeOfType<LcdRenderer>();

        renderer.Filter.ShouldBeSameAs(ExpectedKernel(filter));
        renderer.Order.ShouldBe(order);
    }

    [Fact]
    public void Dispatch_DefaultOptions_KeepTheSharedDefaultRenderer()
    {
        GlyphRenderers.Select(Lcd(16)).ShouldBeSameAs(LcdRenderer.Instance);
        GlyphRenderers.Select(Lcd(16, LcdFilter.Light)).ShouldBeSameAs(LcdRenderer.Instance);
    }

    [Fact]
    public void Dispatch_RejectsUndefinedOptionValues()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => GlyphRenderers.Select(Lcd(16, (LcdFilter)42)));
        Should.Throw<ArgumentOutOfRangeException>(() => GlyphRenderers.Select(Lcd(16, order: (LcdSubpixelOrder)42)));
    }

    [Fact]
    public void LcdOptions_AreIgnoredOutsideLcd()
    {
        using var native = Loaded();
        foreach (char character in "AOg")
        {
            var gray = native.RasterizeGlyph(character, new RasterOptions { Size = 20 })!;
            var tuned = native.RasterizeGlyph(character, new RasterOptions
            {
                Size = 20,
                LcdFilter = LcdFilter.Smooth,
                LcdSubpixelOrder = LcdSubpixelOrder.VBgr,
            })!;

            tuned.Format.ShouldBe(PixelFormat.Grayscale8);
            tuned.BitmapData.ShouldBe(gray.BitmapData);
            tuned.Metrics.ShouldBe(gray.Metrics);
        }
    }

    // ---- Through the rasterizer ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Combinations))]
    public void Rasterizer_MeasuresWhatItRenders_ForEveryCombination(LcdFilter filter, LcdSubpixelOrder order)
    {
        using var native = Loaded();
        var options = Lcd(15, filter, order);

        foreach (char character in Sample)
        {
            var glyph = native.RasterizeGlyph(character, options)!;
            glyph.Format.ShouldBe(PixelFormat.Rgba32, $"'{character}'");
            native.GetGlyphMetrics(character, options).ShouldBe(glyph.Metrics, $"'{character}'");
        }
    }

    [Fact]
    public void Rasterizer_DefaultAndLight_AreTheOutputFromBeforeTheOptionsExisted()
    {
        using var native = Loaded();
        foreach (char character in Sample)
        {
            var unset = native.RasterizeGlyph(character, new RasterOptions { Size = 18, AntiAlias = AntiAliasMode.Lcd })!;
            var light = native.RasterizeGlyph(character, Lcd(18, LcdFilter.Light))!;

            light.BitmapData.ShouldBe(unset.BitmapData, $"'{character}'");
            light.Metrics.ShouldBe(unset.Metrics, $"'{character}'");
        }
    }

    [Theory]
    [InlineData(LcdFilter.Smooth, 1)]
    [InlineData(LcdFilter.None, -1)]
    public void Rasterizer_Filters_ChangeTheSpillAndTheFringes(LcdFilter filter, int widerThanLight)
    {
        // A wider kernel spreads ink further, so Smooth glyphs can only gain columns over Light
        // and None glyphs can only lose them; somewhere in the sample the width must change.
        using var native = Loaded();
        bool changedSomewhere = false;

        foreach (char character in Sample)
        {
            var light = native.RasterizeGlyph(character, Lcd(16, LcdFilter.Light))!;
            var other = native.RasterizeGlyph(character, Lcd(16, filter))!;

            other.BitmapData.ShouldNotBe(light.BitmapData, $"'{character}'");
            Math.Sign(other.Width - light.Width).ShouldBeOneOf([0, widerThanLight], $"'{character}'");
            other.Height.ShouldBe(light.Height, $"'{character}': horizontal panels never add rows");
            changedSomewhere |= other.Width != light.Width;
        }

        changedSomewhere.ShouldBeTrue();
    }

    [Theory]
    [InlineData(LcdSubpixelOrder.Rgb, LcdSubpixelOrder.Bgr)]
    [InlineData(LcdSubpixelOrder.VRgb, LcdSubpixelOrder.VBgr)]
    public void Rasterizer_BlueFirstOrders_AreRedFirstWithRedAndBlueSwapped(LcdSubpixelOrder redFirst, LcdSubpixelOrder blueFirst)
    {
        using var native = Loaded();
        foreach (char character in Sample)
        {
            var a = native.RasterizeGlyph(character, Lcd(16, LcdFilter.Smooth, redFirst))!;
            var b = native.RasterizeGlyph(character, Lcd(16, LcdFilter.Smooth, blueFirst))!;

            b.Metrics.ShouldBe(a.Metrics, $"'{character}'");
            b.BitmapData.ShouldBe(SwapRedBlue(a.BitmapData), $"'{character}'");
        }
    }

    [Fact]
    public void Rasterizer_VerticalOrders_SpreadInkVertically()
    {
        // Vertical panels resolve rows in thirds: horizontal edges carry colour, and the bitmap
        // grows rows (not columns) for the filter's spill.
        using var native = Loaded();
        var gray = native.RasterizeGlyph('-', new RasterOptions { Size = 16 })!;
        var vertical = native.RasterizeGlyph('-', Lcd(16, order: LcdSubpixelOrder.VRgb))!;

        vertical.Width.ShouldBe(gray.Width);
        vertical.Height.ShouldBeGreaterThan(gray.Height);
        bool colored = false;
        for (int i = 0; i < vertical.BitmapData.Length; i += 4)
            colored |= vertical.BitmapData[i] != vertical.BitmapData[i + 2];
        colored.ShouldBeTrue("a hyphen's top and bottom edges must fringe on a vertical panel");
    }

    // ---- Through BmFont.Generate --------------------------------------------------------------

    [Fact]
    public void Generate_CarriesTheOrderFromFontGeneratorOptions()
    {
        var rgb = Generate(o => o.LcdSubpixelOrder = LcdSubpixelOrder.Rgb);
        var bgr = Generate(o => o.LcdSubpixelOrder = LcdSubpixelOrder.Bgr);

        bgr.FntText.ShouldBe(rgb.FntText);
        bgr.Pages[0].PixelData.ShouldBe(SwapRedBlue(rgb.Pages[0].PixelData));
    }

    [Fact]
    public void Generate_CarriesTheFilterFromFontGeneratorOptions()
    {
        var light = Generate(o => o.LcdFilter = LcdFilter.Light);
        var smooth = Generate(o => o.LcdFilter = LcdFilter.Smooth);

        Generate(_ => { }).Pages[0].PixelData.ShouldBe(light.Pages[0].PixelData);
        smooth.FntText.ShouldNotBe(light.FntText, "Smooth glyphs are wider, so their char widths change");
    }

    [Theory]
    [InlineData(2, LcdSubpixelOrder.Rgb, LcdFilter.Default)]
    [InlineData(3, LcdSubpixelOrder.Bgr, LcdFilter.Smooth)]
    [InlineData(4, LcdSubpixelOrder.VRgb, LcdFilter.None)]
    public void Generate_Lcd_WithSuperSampling_IsTheFinalSizeLcdRender(int level, LcdSubpixelOrder order, LcdFilter filter)
    {
        // Super sampling renders at N x size and box-filters down. For LCD that would average
        // the subpixel pattern away (each channel smeared across N pixels), so core renders LCD
        // at the final size: the output equals the non-supersampled LCD output exactly.
        var plain = Generate(o => { o.LcdSubpixelOrder = order; o.LcdFilter = filter; });
        var supersampled = Generate(o => { o.LcdSubpixelOrder = order; o.LcdFilter = filter; o.SuperSampleLevel = level; });

        supersampled.FntText.ShouldBe(plain.FntText);
        supersampled.Pages.Count.ShouldBe(plain.Pages.Count);
        for (int p = 0; p < plain.Pages.Count; p++)
            supersampled.Pages[p].PixelData.ShouldBe(plain.Pages[p].PixelData, $"page {p}");
    }

    [Fact]
    public void Generate_Grayscale_WithSuperSampling_StillSupersamples()
    {
        // Guard for the test above: super sampling must still change non-LCD output.
        var plain = Generate(o => o.AntiAlias = AntiAliasMode.Grayscale);
        var supersampled = Generate(o => { o.AntiAlias = AntiAliasMode.Grayscale; o.SuperSampleLevel = 2; });

        supersampled.Pages[0].PixelData.ShouldNotBe(plain.Pages[0].PixelData);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static NativeRasterizer Loaded()
    {
        var native = new NativeRasterizer();
        native.LoadFont(TestFonts.RobotoRegularBytes());
        return native;
    }

    private static BmFontResult Generate(Action<FontGeneratorOptions> configure)
    {
        var options = new FontGeneratorOptions
        {
            Size = 20,
            Characters = CharacterSet.FromChars("AVWTaeoy.,!?0123&@-"),
            Backend = RasterizerBackend.Native,
            AntiAlias = AntiAliasMode.Lcd,
        };
        configure(options);
        return BmFont.Generate(TestFonts.RobotoRegularBytes(), options);
    }

    private static byte[] SwapRedBlue(byte[] rgba)
    {
        var swapped = (byte[])rgba.Clone();
        for (int i = 0; i < swapped.Length; i += 4)
            (swapped[i], swapped[i + 2]) = (swapped[i + 2], swapped[i]);
        return swapped;
    }
}
