using KernSmith.Output;
using KernSmith.Output.Model;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 165 — drives the whole pipeline (load, rasterize, pack, format) through the native
/// backend and checks the result is a real BMFont file, not just a non-crashing call.
/// </summary>
public class NativeEndToEndTests
{
    private static FontGeneratorOptions Options(RasterizerBackend backend) => new()
    {
        Size = 32,
        Characters = CharacterSet.Ascii,
        Backend = backend
    };

    [Fact]
    public void Generate_WithNativeBackend_ProducesParseableBmFont()
    {
        var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), Options(RasterizerBackend.Native));

        result.ShouldNotBeNull();
        result.Pages.Count.ShouldBeGreaterThan(0);

        var model = BmFontReader.ReadText(result.FntText);
        model.Characters.Count.ShouldBeGreaterThan(90, "ASCII should yield most of its printable glyphs");
        model.Common.LineHeight.ShouldBeGreaterThan(0);
        model.Common.Base.ShouldBeGreaterThan(0);

        // 'A' must be a real, non-degenerate cell.
        var upperA = model.Characters.First(c => c.Id == 'A');
        upperA.Width.ShouldBeGreaterThan(0);
        upperA.Height.ShouldBeGreaterThan(0);
        upperA.XAdvance.ShouldBeGreaterThan(0);

        // Space carries an advance but no bitmap.
        var space = model.Characters.First(c => c.Id == ' ');
        space.XAdvance.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Generate_WithNativeBackend_ProducesInkedAtlas()
    {
        var result = BmFont.Generate(TestFonts.RobotoRegularBytes(), Options(RasterizerBackend.Native));

        // A backend that silently rendered nothing would still produce a valid .fnt, so
        // check the texture actually has ink in it.
        result.Pages[0].PixelData.Length.ShouldBeGreaterThan(0);
        result.Pages[0].PixelData.ShouldContain(b => b != 0);
    }

    [Fact]
    public void Generate_WithNativeBackend_MetricsTrackFreeType()
    {
        var bytes = TestFonts.RobotoRegularBytes();

        var native = BmFontReader.ReadText(BmFont.Generate(bytes, Options(RasterizerBackend.Native)).FntText);
        var freeType = BmFontReader.ReadText(BmFont.Generate(bytes, Options(RasterizerBackend.FreeType)).FntText);

        ShouldBeWithinOne(native.Common.LineHeight, freeType.Common.LineHeight, "lineHeight");
        ShouldBeWithinOne(native.Common.Base, freeType.Common.Base, "base");

        var nativeA = native.Characters.First(c => c.Id == 'A');
        var freeTypeA = freeType.Characters.First(c => c.Id == 'A');
        ShouldBeWithinOne(nativeA.XAdvance, freeTypeA.XAdvance, "'A' xadvance");
        ShouldBeWithinOne(nativeA.Width, freeTypeA.Width, "'A' width");
        ShouldBeWithinOne(nativeA.Height, freeTypeA.Height, "'A' height");
    }

    private static void ShouldBeWithinOne(int actual, int expected, string what) =>
        Math.Abs(actual - expected).ShouldBeLessThanOrEqualTo(1,
            $"{what}: native {actual} vs FreeType {expected}");
}
