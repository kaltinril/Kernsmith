using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// With <see cref="FontGeneratorOptions.Variants"/>, <see cref="FontGeneratorOptions.AutofitTexture"/>
/// grows the shared atlas the way <c>Generate</c> does without variants: once the width is at its
/// maximum, the height grows instead.
/// </summary>
public sealed class AtlasVariantAutofitTests
{
    private static byte[] Font() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Fact]
    public void Autofit_WidthAtItsMaximum_GrowsTheHeight()
    {
        // 128 px wide at most: the estimate (128x512) is too small for ASCII plus a shadow
        // variant, and doubling the already-capped width cannot help.
        var result = BmFont.Generate(Font(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii,
            Rasterizer = new StbTrueTypeRasterizer(),
            AutofitTexture = true,
            MaxTextureWidth = 128,
            MaxTextureHeight = 2048,
            Variants = [new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette, BlurRadius: 2)],
        });

        result.Pages.Count.ShouldBe(1);
        result.Pages[0].Width.ShouldBe(128);
    }
}
