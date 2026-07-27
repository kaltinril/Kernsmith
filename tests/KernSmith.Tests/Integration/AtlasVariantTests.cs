using KernSmith.Output.Model;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// Tests for <see cref="AtlasVariant"/> (phase-181, issue #175): an additional character-set
/// rendering (e.g. a dropshadow silhouette) packed alongside the primary font, generated as
/// its own <see cref="BmFontModel"/> with no baked offset or color.
/// </summary>
[Collection("RasterizerFactory")]
public class AtlasVariantTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Fact]
    public void NoVariants_ProducesEmptyVariantModels()
    {
        var options = new FontGeneratorOptions { Size = 32, Characters = CharacterSet.FromChars("O") };

        var result = BmFont.Generate(LoadTestFont(), options);

        result.VariantModels.ShouldBeEmpty();
        result.VariantPages.ShouldBeEmpty();
    }

    [Fact]
    public void ShadowSilhouetteVariant_ProducesOneCharEntryPerCodepoint()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("ABC"),
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette, BlurRadius: 1) }
        };

        var result = BmFont.Generate(LoadTestFont(), options);

        result.VariantModels.TryGetValue("shadow", out var variantModel).ShouldBeTrue();
        variantModel.ShouldNotBeNull();
        result.VariantPages.ContainsKey("shadow").ShouldBeTrue();

        var requestedCodepoints = new[] { 'A', 'B', 'C' }.Select(c => (int)c).ToHashSet();
        var variantCodepoints = variantModel.Characters.Select(c => c.Id).ToHashSet();
        variantCodepoints.ShouldBe(requestedCodepoints, ignoreOrder: true);

        // Same page filenames referenced by the variant's Pages list are its own (not shared
        // with the primary in this implementation — see phase-181 doc deviations).
        variantModel.Pages.ShouldNotBeEmpty();
    }

    [Fact]
    public void ShadowSilhouetteVariant_HasNoBakedOffset()
    {
        // A non-zero shadow offset on the primary must not leak into the variant's own
        // xoffset/yoffset — the silhouette is centered on the glyph's own bounding box,
        // not shifted, so the runtime is free to translate it however it wants.
        var chars = CharacterSet.FromChars("O");

        var noOffsetOptions = new FontGeneratorOptions
        {
            Size = 48,
            Characters = chars,
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) }
        };
        var noOffsetResult = BmFont.Generate(LoadTestFont(), noOffsetOptions);
        var noOffsetChar = noOffsetResult.VariantModels["shadow"].Characters.Single();

        var offsetOptions = new FontGeneratorOptions
        {
            Size = 48,
            Characters = chars,
            ShadowOffsetX = 6,
            ShadowOffsetY = -6,
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) }
        };
        var offsetResult = BmFont.Generate(LoadTestFont(), offsetOptions);
        var offsetChar = offsetResult.VariantModels["shadow"].Characters.Single();

        offsetChar.XOffset.ShouldBe(noOffsetChar.XOffset);
        offsetChar.YOffset.ShouldBe(noOffsetChar.YOffset);
        offsetChar.Width.ShouldBe(noOffsetChar.Width);
        offsetChar.Height.ShouldBe(noOffsetChar.Height);
    }

    [Fact]
    public void ShadowSilhouetteVariant_DoesNotAffectPrimaryPixels()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("O"),
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) }
        };

        var withVariant = BmFont.Generate(LoadTestFont(), options);
        var withoutVariant = BmFont.Generate(LoadTestFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("O")
        });

        withVariant.Pages[0].PixelData.ShouldBe(withoutVariant.Pages[0].PixelData);
    }

    [Fact]
    public void PrimaryAndVariantModels_HaveSiblingLinkage()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("O"),
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) }
        };

        var result = BmFont.Generate(LoadTestFont(), options);

        result.Model.Extended.ShouldNotBeNull();
        result.Model.Extended!.Variants.ShouldNotBeNull();
        result.Model.Extended!.Variants!.ShouldContain("shadow");

        var variantModel = result.VariantModels["shadow"];
        variantModel.Extended.ShouldNotBeNull();
        variantModel.Extended!.VariantOf.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void TargetRegion_WithVariants_Throws()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("O"),
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) },
            TargetRegion = new AtlasTargetRegion
            {
                SourcePngData = new byte[] { 1, 2, 3 },
                X = 0,
                Y = 0,
                Width = 64,
                Height = 64
            }
        };

        Should.Throw<NotSupportedException>(() => BmFont.Generate(LoadTestFont(), options));
    }
}
