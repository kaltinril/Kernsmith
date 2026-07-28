using KernSmith.Output;
using KernSmith.Output.Model;
using Shouldly;

namespace KernSmith.Tests.Integration;

/// <summary>
/// Tests for <see cref="AtlasVariant"/> (phase-182, issue #175): an additional character-set
/// rendering (e.g. a dropshadow silhouette) packed into the SAME shared atlas PNG as the
/// primary font (via <see cref="KernSmith.Atlas.AtlasGroupBuilder"/>), generated as its own
/// <see cref="BmFontModel"/> with no baked offset or color.
/// </summary>
[Collection("RasterizerFactory")]
public class AtlasVariantTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    /// <summary>
    /// Extracts a sub-rectangle's per-pixel coverage value (alpha for RGBA pages, the raw byte
    /// for grayscale pages) so regions can be compared regardless of whether the page happens
    /// to be grayscale or RGBA (a shared atlas with a shadow variant promotes to RGBA even
    /// though the primary-only atlas would have stayed grayscale).
    /// </summary>
    private static byte[] ExtractCoverageRegion(byte[] pixelData, int pageWidth, PixelFormat format, int x, int y, int width, int height)
    {
        var bytesPerPixel = format == PixelFormat.Rgba32 ? 4 : 1;
        var alphaOffset = format == PixelFormat.Rgba32 ? 3 : 0;

        var region = new byte[width * height];
        for (var row = 0; row < height; row++)
        {
            for (var col = 0; col < width; col++)
            {
                var srcIdx = ((y + row) * pageWidth + (x + col)) * bytesPerPixel + alphaOffset;
                region[row * width + col] = pixelData[srcIdx];
            }
        }
        return region;
    }

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

        // phase-182: the variant is packed into the SAME shared atlas as the primary, so its
        // Pages list references the exact same filenames as the primary model's Pages.
        variantModel.Pages.ShouldNotBeEmpty();
        variantModel.Pages.Select(p => p.File).ShouldBe(result.Model.Pages.Select(p => p.File));
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
    public void ShadowSilhouetteVariant_DoesNotAlterPrimaryGlyphPixels()
    {
        // phase-182: the primary and shadow glyphs now share one page, so the shadow's
        // presence can shift where the primary glyph itself lands within that page (more
        // rects packed together). What must NOT change is the primary glyph's own rendered
        // pixel content — so compare sub-regions extracted at each run's own placement,
        // not the whole page buffer.
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

        var withChar = withVariant.Model.Characters.Single(c => c.Id == 'O');
        var withoutChar = withoutVariant.Model.Characters.Single(c => c.Id == 'O');

        withChar.Width.ShouldBe(withoutChar.Width);
        withChar.Height.ShouldBe(withoutChar.Height);

        var withPage = withVariant.Pages[withChar.Page];
        var withoutPage = withoutVariant.Pages[withoutChar.Page];

        var withRegion = ExtractCoverageRegion(withPage.PixelData, withPage.Width, withPage.Format, withChar.X, withChar.Y, withChar.Width, withChar.Height);
        var withoutRegion = ExtractCoverageRegion(withoutPage.PixelData, withoutPage.Width, withoutPage.Format, withoutChar.X, withoutChar.Y, withoutChar.Width, withoutChar.Height);

        withRegion.ShouldBe(withoutRegion);
    }

    [Fact]
    public void ShadowSilhouetteVariant_CoveredPixelsAreWhiteSoRuntimeTintingWorks()
    {
        // The shadow silhouette variant is packed as an ordinary RGBA glyph, and consumers
        // multiply it by their own tint color at draw time. If the coverage were baked in as
        // black (RGB=0,0,0), black * anyTint == black and no consumer could ever recolor it.
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("O"),
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) }
        };

        var result = BmFont.Generate(LoadTestFont(), options);
        var variantModel = result.VariantModels["shadow"];
        var shadowChar = variantModel.Characters.Single(c => c.Id == 'O');
        var shadowPage = result.VariantPages["shadow"][shadowChar.Page];

        shadowPage.Format.ShouldBe(PixelFormat.Rgba32);

        var foundCoveredPixel = false;
        for (var row = 0; row < shadowChar.Height; row++)
        {
            for (var col = 0; col < shadowChar.Width; col++)
            {
                var srcIdx = ((shadowChar.Y + row) * shadowPage.Width + (shadowChar.X + col)) * 4;
                var r = shadowPage.PixelData[srcIdx];
                var g = shadowPage.PixelData[srcIdx + 1];
                var b = shadowPage.PixelData[srcIdx + 2];
                var a = shadowPage.PixelData[srcIdx + 3];

                if (a > 0)
                {
                    foundCoveredPixel = true;
                    r.ShouldBe((byte)255, $"pixel ({col},{row}) had alpha {a} but R={r}");
                    g.ShouldBe((byte)255, $"pixel ({col},{row}) had alpha {a} but G={g}");
                    b.ShouldBe((byte)255, $"pixel ({col},{row}) had alpha {a} but B={b}");
                }
            }
        }

        foundCoveredPixel.ShouldBeTrue("expected at least one covered (alpha > 0) pixel in the shadow silhouette region");
    }

    [Fact]
    public void ShadowSilhouetteVariant_SharesOnePngWithNonOverlappingRegions()
    {
        // Pixel-level proof (not just placement math) that primary and shadow glyphs both
        // land in the same shared PNG at non-overlapping regions.
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("ABC"),
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) }
        };

        var result = BmFont.Generate(LoadTestFont(), options);
        var variantModel = result.VariantModels["shadow"];

        // Same physical page count/dimensions -- it's the same atlas.
        result.Pages.Count.ShouldBe(result.VariantPages["shadow"].Count);
        for (var i = 0; i < result.Pages.Count; i++)
        {
            result.Pages[i].Width.ShouldBe(result.VariantPages["shadow"][i].Width);
            result.Pages[i].Height.ShouldBe(result.VariantPages["shadow"][i].Height);
            result.Pages[i].PixelData.ShouldBeSameAs(result.VariantPages["shadow"][i].PixelData);
        }

        // No primary char rect overlaps any shadow char rect on the same page.
        foreach (var primaryChar in result.Model.Characters)
        {
            foreach (var shadowChar in variantModel.Characters.Where(c => c.Page == primaryChar.Page))
            {
                var overlaps =
                    primaryChar.X < shadowChar.X + shadowChar.Width &&
                    primaryChar.X + primaryChar.Width > shadowChar.X &&
                    primaryChar.Y < shadowChar.Y + shadowChar.Height &&
                    primaryChar.Y + primaryChar.Height > shadowChar.Y;

                overlaps.ShouldBeFalse(
                    $"primary char {primaryChar.Id} and shadow char {shadowChar.Id} overlap on page {primaryChar.Page}");
            }
        }
    }

    [Fact]
    public void ShadowSilhouetteVariant_ToFile_WritesOnlyOnePngSharedByBothFntFiles()
    {
        var options = new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.FromChars("O"),
            Variants = new[] { new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette) }
        };

        var result = BmFont.Generate(LoadTestFont(), options);

        var tempDir = Path.Join(Path.GetTempPath(), $"KernSmith_ShadowVariant_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var outputPath = Path.Join(tempDir, "testfont");
            result.ToFile(outputPath);

            File.Exists(outputPath + ".fnt").ShouldBeTrue();
            File.Exists(outputPath + "-shadow.fnt").ShouldBeTrue();

            var pngFiles = Directory.GetFiles(tempDir, "*.png");
            pngFiles.Length.ShouldBe(result.Pages.Count, "primary and shadow must share the same PNG file(s), not duplicate them");

            var primaryFntText = File.ReadAllText(outputPath + ".fnt");
            var shadowFntText = File.ReadAllText(outputPath + "-shadow.fnt");

            var primaryPageLine = primaryFntText.Split('\n').Single(l => l.StartsWith("page "));
            var shadowPageLine = shadowFntText.Split('\n').Single(l => l.StartsWith("page "));
            primaryPageLine.ShouldBe(shadowPageLine, "both .fnt files must reference the identical shared PNG filename");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
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
