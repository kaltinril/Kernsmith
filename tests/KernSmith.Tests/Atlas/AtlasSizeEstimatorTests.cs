using KernSmith.Atlas;
using Shouldly;

namespace KernSmith.Tests.Atlas;

public class AtlasSizeEstimatorTests
{
    private static readonly AtlasSizingOptions DefaultOptions = new()
    {
        PowerOfTwo = true,
        AllowNonSquare = true,
        MaxWidth = 4096,
        MaxHeight = 4096,
        MinSize = 64
    };

    private static GlyphRect Rect(int id, int w, int h) => new(id, w, h);

    // ---------------------------------------------------------------
    // 1. Empty input returns (MinSize, MinSize)
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_EmptyInput_ReturnsMinSizeDimensions()
    {
        // Arrange
        var rects = Array.Empty<GlyphRect>();
        var options = new AtlasSizingOptions { MinSize = 64, PowerOfTwo = false };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        width.ShouldBe(64);
        height.ShouldBe(64);
    }

    [Fact]
    public void Estimate_EmptyInput_PowerOfTwo_ReturnsNextPotOfMinSize()
    {
        // Arrange
        var rects = Array.Empty<GlyphRect>();
        var options = new AtlasSizingOptions { MinSize = 50, PowerOfTwo = true };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        width.ShouldBe(64);
        height.ShouldBe(64);
    }

    // ---------------------------------------------------------------
    // 2. Single glyph returns glyph dimensions + POT rounding
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_SingleGlyph_PotMode_ReturnsPowerOfTwoDimensions()
    {
        // Arrange
        var rects = new[] { Rect(1, 20, 30) };
        var options = new AtlasSizingOptions { PowerOfTwo = true, MinSize = 32 };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        width.ShouldBeGreaterThanOrEqualTo(20, "width must fit the glyph");
        height.ShouldBeGreaterThanOrEqualTo(30, "height must fit the glyph");
        IsPowerOfTwo(width).ShouldBeTrue("width should be a power of two");
        IsPowerOfTwo(height).ShouldBeTrue("height should be a power of two");
    }

    // ---------------------------------------------------------------
    // 3. Uniform-height glyphs -- estimate matches simple row calculation
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_UniformHeightGlyphs_ReasonableEstimate()
    {
        // Arrange -- 10 glyphs, each 16x16
        var rects = Enumerable.Range(0, 10).Select(i => Rect(i, 16, 16)).ToArray();
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 16,
            MaxWidth = 1024,
            MaxHeight = 1024
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert -- total area is 10*16*16 = 2560; estimate should be reasonable
        long estimatedArea = (long)width * height;
        estimatedArea.ShouldBeGreaterThanOrEqualTo(2560,
            "estimated area must be at least total glyph area");
    }

    // ---------------------------------------------------------------
    // 4. Mixed-height glyphs -- estimate >= actual total area
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_MixedHeightGlyphs_DoesNotUnderestimateTotalArea()
    {
        // Arrange
        var rects = new[]
        {
            Rect(1, 10, 50),
            Rect(2, 30, 10),
            Rect(3, 20, 30),
            Rect(4, 15, 25),
            Rect(5, 40, 15)
        };
        long totalArea = rects.Sum(r => (long)r.Width * r.Height);
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 1,
            MaxWidth = 4096,
            MaxHeight = 4096
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        long estimatedArea = (long)width * height;
        estimatedArea.ShouldBeGreaterThanOrEqualTo(totalArea,
            "estimate must never be smaller than total glyph area");
    }

    // ---------------------------------------------------------------
    // 5. Channel packing -- area is divided by 4, produces smaller atlas
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_ChannelPacking_ProducesSmallerOrEqualAtlas()
    {
        // Arrange -- many glyphs with varied sizes to see a clear difference
        var rects = Enumerable.Range(0, 200).Select(i => Rect(i, 8 + (i % 5), 12 + (i % 3))).ToArray();
        var baseOptions = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            ChannelPacking = false,
            MinSize = 1,
            MaxWidth = 4096,
            MaxHeight = 4096
        };
        var packedOptions = baseOptions with { ChannelPacking = true };

        // Act
        var (wNormal, hNormal) = AtlasSizeEstimator.Estimate(rects, baseOptions);
        var (wPacked, hPacked) = AtlasSizeEstimator.Estimate(rects, packedOptions);

        // Assert
        long normalArea = (long)wNormal * hNormal;
        long packedArea = (long)wPacked * hPacked;
        packedArea.ShouldBeLessThanOrEqualTo(normalArea,
            "channel packing should produce a smaller or equal atlas than without");

        // Also verify the channel-packed estimate covers at least 1/4 of total glyph area
        long totalGlyphArea = rects.Sum(r => (long)r.Width * r.Height);
        long channelAdjustedArea = (totalGlyphArea + 3) / 4;
        packedArea.ShouldBeGreaterThanOrEqualTo(channelAdjustedArea,
            "channel-packed atlas must still cover the channel-adjusted glyph area");
    }

    // ---------------------------------------------------------------
    // 6. Equalized cell heights -- uses 1D strip formula
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_EqualizedCellHeights_ProducesValidResult()
    {
        // Arrange -- all same height (equalized)
        var rects = Enumerable.Range(0, 20).Select(i => Rect(i, 12, 16)).ToArray();
        var options = new AtlasSizingOptions
        {
            EqualizedCellHeights = true,
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 1,
            MaxWidth = 512,
            MaxHeight = 512
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        long totalArea = rects.Sum(r => (long)r.Width * r.Height);
        long estimatedArea = (long)width * height;
        estimatedArea.ShouldBeGreaterThanOrEqualTo(totalArea,
            "equalized cell estimate must cover all glyphs");
        width.ShouldBeGreaterThanOrEqualTo(12, "width must fit at least one cell");
        height.ShouldBeGreaterThanOrEqualTo(16, "height must fit at least one row");
    }

    // ---------------------------------------------------------------
    // 7. Zero-area glyphs in list -- no crash or NaN
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_ZeroAreaGlyphs_DoesNotCrash()
    {
        // Arrange
        var rects = new[]
        {
            Rect(1, 0, 0),
            Rect(2, 10, 0),
            Rect(3, 0, 10),
            Rect(4, 20, 20)
        };
        var options = DefaultOptions;

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        width.ShouldBeGreaterThan(0);
        height.ShouldBeGreaterThan(0);
        width.ShouldBeGreaterThanOrEqualTo(20, "must fit the 20x20 glyph");
        height.ShouldBeGreaterThanOrEqualTo(20, "must fit the 20x20 glyph");
    }

    [Fact]
    public void Estimate_AllZeroAreaGlyphs_ReturnMinSize()
    {
        // Arrange
        var rects = new[]
        {
            Rect(1, 0, 0),
            Rect(2, 0, 10),
            Rect(3, 5, 0)
        };
        var options = new AtlasSizingOptions { MinSize = 64, PowerOfTwo = false };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert -- all zero-area, treated as empty
        width.ShouldBe(64);
        height.ShouldBe(64);
    }

    // ---------------------------------------------------------------
    // 8. POT mode -- result dimensions are powers of two
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_PotMode_ResultDimensionsArePowersOfTwo()
    {
        // Arrange
        var rects = Enumerable.Range(0, 30).Select(i => Rect(i, 15, 18)).ToArray();
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = true,
            AllowNonSquare = true,
            MinSize = 32,
            MaxWidth = 4096,
            MaxHeight = 4096
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        IsPowerOfTwo(width).ShouldBeTrue($"width {width} should be a power of two");
        IsPowerOfTwo(height).ShouldBeTrue($"height {height} should be a power of two");
    }

    // ---------------------------------------------------------------
    // 9. Non-POT mode -- result dimensions can be arbitrary
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_NonPotMode_DimensionsCanBeArbitrary()
    {
        // Arrange -- use dimensions that don't naturally land on POT
        var rects = new[]
        {
            Rect(1, 13, 17),
            Rect(2, 11, 19),
            Rect(3, 14, 21)
        };
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 1,
            MaxWidth = 4096,
            MaxHeight = 4096
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert -- just verify it returns valid results, not necessarily POT
        width.ShouldBeGreaterThan(0);
        height.ShouldBeGreaterThan(0);
        // At least one dimension should be allowed to be non-POT
        // (this is a negative test -- we do not require POT)
    }

    // ---------------------------------------------------------------
    // 10. Non-square -- W and H can differ, total pixels <= square estimate
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_NonSquareAllowed_CanProduceDifferentWidthAndHeight()
    {
        // Arrange -- wide glyphs to encourage non-square output
        var rects = Enumerable.Range(0, 20).Select(i => Rect(i, 40, 10)).ToArray();
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = true,
            AllowNonSquare = true,
            MinSize = 32,
            MaxWidth = 4096,
            MaxHeight = 4096
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        width.ShouldBeGreaterThan(0);
        height.ShouldBeGreaterThan(0);
        // Non-square atlas may (but is not required to) have different W and H
    }

    [Fact]
    public void Estimate_NonSquare_AreaNotLargerThanSquare()
    {
        // Arrange
        var rects = Enumerable.Range(0, 50).Select(i => Rect(i, 12, 14)).ToArray();
        var squareOptions = new AtlasSizingOptions
        {
            PowerOfTwo = true,
            AllowNonSquare = false,
            MinSize = 32,
            MaxWidth = 4096,
            MaxHeight = 4096
        };
        var nonSquareOptions = squareOptions with { AllowNonSquare = true };

        // Act
        var (wSq, hSq) = AtlasSizeEstimator.Estimate(rects, squareOptions);
        var (wNs, hNs) = AtlasSizeEstimator.Estimate(rects, nonSquareOptions);

        // Assert
        long squareArea = (long)wSq * hSq;
        long nonSquareArea = (long)wNs * hNs;
        nonSquareArea.ShouldBeLessThanOrEqualTo(squareArea,
            "non-square atlas should not waste more space than a square atlas");
    }

    // ---------------------------------------------------------------
    // 11. Large glyph set -- verify no integer overflow
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_LargeGlyphSet_NoIntegerOverflow()
    {
        // Arrange -- 10,000 glyphs
        var rects = Enumerable.Range(0, 10_000).Select(i => Rect(i, 8, 8)).ToArray();
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 1,
            MaxWidth = 16384,
            MaxHeight = 16384
        };

        // Act
        var act = () => AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        var (width, height) = act();
        width.ShouldBeGreaterThan(0);
        height.ShouldBeGreaterThan(0);
        long totalGlyphArea = 10_000L * 8 * 8;
        ((long)width * height).ShouldBeGreaterThanOrEqualTo(totalGlyphArea);
    }

    // ---------------------------------------------------------------
    // 12. MaxTexture clamping -- result doesn't exceed max dimensions
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_MaxTextureClamping_DoesNotExceedMaxDimensions()
    {
        // Arrange -- many glyphs that would want a large atlas
        var rects = Enumerable.Range(0, 500).Select(i => Rect(i, 20, 20)).ToArray();
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 32,
            MaxWidth = 256,
            MaxHeight = 256
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        width.ShouldBeLessThanOrEqualTo(256, "width must not exceed MaxWidth");
        height.ShouldBeLessThanOrEqualTo(256, "height must not exceed MaxHeight");
    }

    [Fact]
    public void Estimate_MaxTextureClamping_PotMode_DoesNotExceedMaxDimensions()
    {
        // Arrange
        var rects = Enumerable.Range(0, 500).Select(i => Rect(i, 20, 20)).ToArray();
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = true,
            AllowNonSquare = true,
            MinSize = 32,
            MaxWidth = 256,
            MaxHeight = 256
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        width.ShouldBeLessThanOrEqualTo(256, "POT width must not exceed MaxWidth");
        height.ShouldBeLessThanOrEqualTo(256, "POT height must not exceed MaxHeight");
    }

    // ---------------------------------------------------------------
    // 13. Single glyph wider than tall -- handled correctly
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_SingleGlyphWiderThanTall_HandledCorrectly()
    {
        // Arrange
        var rects = new[] { Rect(1, 100, 20) };
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 1,
            MaxWidth = 4096,
            MaxHeight = 4096
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        width.ShouldBeGreaterThanOrEqualTo(100, "width must fit the wide glyph");
        height.ShouldBeGreaterThanOrEqualTo(20, "height must fit the glyph height");
    }

    // ---------------------------------------------------------------
    // 14. Estimate is reasonable -- within 2x of total area for typical set
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_TypicalAsciiSet_WithinTwoTimesTotalArea()
    {
        // Arrange -- simulate typical ASCII glyph sizes (varying widths, similar heights)
        var random = new Random(42); // Deterministic seed
        var rects = Enumerable.Range(0, 95).Select(i =>
            Rect(i + 32, random.Next(6, 20), random.Next(16, 24))).ToArray();
        long totalArea = rects.Sum(r => (long)r.Width * r.Height);
        var options = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 1,
            MaxWidth = 4096,
            MaxHeight = 4096,
            PackingEfficiency = 0.90f
        };

        // Act
        var (width, height) = AtlasSizeEstimator.Estimate(rects, options);

        // Assert
        long estimatedArea = (long)width * height;
        estimatedArea.ShouldBeGreaterThanOrEqualTo(totalArea,
            "estimate must not underestimate");
        estimatedArea.ShouldBeLessThanOrEqualTo(totalArea * 2,
            "estimate should be within 2x of total glyph area for a typical set");
    }

    // ---------------------------------------------------------------
    // 15. PackingEfficiency affects result -- lower efficiency produces larger atlas
    // ---------------------------------------------------------------

    [Fact]
    public void Estimate_LowerPackingEfficiency_ProducesLargerOrEqualAtlas()
    {
        // Arrange
        var rects = Enumerable.Range(0, 60).Select(i => Rect(i, 12, 14)).ToArray();
        var highEffOptions = new AtlasSizingOptions
        {
            PowerOfTwo = false,
            AllowNonSquare = true,
            MinSize = 1,
            MaxWidth = 4096,
            MaxHeight = 4096,
            PackingEfficiency = 0.95f
        };
        var lowEffOptions = highEffOptions with { PackingEfficiency = 0.55f };

        // Act
        var (wHigh, hHigh) = AtlasSizeEstimator.Estimate(rects, highEffOptions);
        var (wLow, hLow) = AtlasSizeEstimator.Estimate(rects, lowEffOptions);

        // Assert
        long highEffArea = (long)wHigh * hHigh;
        long lowEffArea = (long)wLow * hLow;
        lowEffArea.ShouldBeGreaterThanOrEqualTo(highEffArea,
            "lower packing efficiency should produce a larger or equal atlas");
    }

    // ---------------------------------------------------------------
    // Additional edge case: EstimateShelfHeight internal method
    // ---------------------------------------------------------------

    [Fact]
    public void EstimateShelfHeight_EmptyInput_ReturnsZero()
    {
        // Arrange
        var glyphs = ReadOnlySpan<GlyphRect>.Empty;

        // Act
        var result = AtlasSizeEstimator.EstimateShelfHeight(glyphs, 256);

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public void EstimateShelfHeight_ZeroWidth_ReturnsZero()
    {
        // Arrange
        var glyphs = new[] { Rect(1, 10, 20) }.AsSpan();

        // Act
        var result = AtlasSizeEstimator.EstimateShelfHeight(glyphs, 0);

        // Assert
        result.ShouldBe(0);
    }

    [Fact]
    public void EstimateShelfHeight_SingleGlyph_ReturnsHeightWithMargin()
    {
        // Arrange
        var glyphs = new[] { Rect(1, 10, 20) }.AsSpan();

        // Act
        var result = AtlasSizeEstimator.EstimateShelfHeight(glyphs, 256);

        // Assert -- should be 20 * 1.05 = 21
        result.ShouldBe(21);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;
}
