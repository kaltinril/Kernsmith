using KernSmith.Font.Tables;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Variations;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// A CFF2 variable font with variation axes set. Until CFF2 <c>blend</c> applies deltas (Phase
/// 171 Wave 2) Native renders its default instance — and must not fail just because axes were
/// requested, since <c>SupportsVariableFonts</c> is true and core forwards them.
/// </summary>
public class NativeCff2VariationTests
{
    private static readonly Dictionary<string, float> Black = new() { ["wght"] = 900 };

    private static IReadOnlyList<VariationAxis> FvarAxes(byte[] bytes) =>
        BmFont.ReadFontInfo(bytes).VariationAxes ?? throw new InvalidOperationException("not a variable font");

    [Fact]
    public void OutlineDecoders_HandTheVariationStateToTheCffDecoder()
    {
        // The CFF2 blend wave computes CharstringContext.BlendScalars from these coordinates.
        var face = NativeFontFace.Load(TestFonts.SourceCodeVariableOtfBytes());
        var variations = FontVariations.Load(face);
        variations.ShouldNotBeNull();

        var decoder = OutlineDecoders.ForFace(face, variations).ShouldBeOfType<CffOutlineDecoder>();
        decoder.Variations.ShouldBeSameAs(variations);
    }

    [Fact]
    public void SetVariationAxes_OnACff2Font_RendersTheDefaultInstance()
    {
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        var options = new RasterOptions { Size = 32 };

        using var plain = new NativeRasterizer();
        plain.LoadFont(bytes);
        using var varied = new NativeRasterizer();
        varied.LoadFont(bytes);
        varied.SetVariationAxes(FvarAxes(bytes), Black);

        for (int c = 33; c < 127; c++)
        {
            var expected = plain.RasterizeGlyph(c, options);
            var actual = varied.RasterizeGlyph(c, options);
            expected.ShouldNotBeNull();
            actual.ShouldNotBeNull();
            actual!.BitmapData.ShouldBe(expected!.BitmapData, $"U+{c:X4}");
            actual.Metrics.ShouldBe(expected.Metrics, $"U+{c:X4}");
        }
    }

    [Fact]
    public void Generate_WithVariationAxes_OnACff2Font_ProducesTheDefaultInstanceGlyphs()
    {
        // Through the public pipeline, which forwards VariationAxes because Native advertises
        // SupportsVariableFonts.
        var bytes = TestFonts.SourceCodeVariableOtfBytes();
        FontGeneratorOptions Options(Dictionary<string, float>? axes) => new()
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Backend = RasterizerBackend.Native,
            VariationAxes = axes,
        };

        var varied = BmFontReader.ReadText(BmFont.Generate(bytes, Options(Black)).FntText);
        var plain = BmFontReader.ReadText(BmFont.Generate(bytes, Options(null)).FntText);

        varied.Characters.Count.ShouldBe(plain.Characters.Count);
        foreach (var expected in plain.Characters)
        {
            var actual = varied.Characters.First(c => c.Id == expected.Id);
            actual.Width.ShouldBe(expected.Width, $"U+{expected.Id:X4} width");
            actual.Height.ShouldBe(expected.Height, $"U+{expected.Id:X4} height");
            actual.XAdvance.ShouldBe(expected.XAdvance, $"U+{expected.Id:X4} xadvance");
        }
    }
}
