using KernSmith.Atlas;
using Shouldly;

namespace KernSmith.Tests.Atlas;

public class AtlasGroupBuilderTests
{
    private static GlyphRect Rect(int id, int w, int h) => new(id, w, h);

    // ---------------------------------------------------------------
    // 1. Overlapping ids across sources -- no collision, correct keys/counts
    // ---------------------------------------------------------------

    [Fact]
    public void Pack_TwoSourcesWithOverlappingIds_SplitsPlacementsByTagWithOriginalIds()
    {
        // Arrange -- both sources use the same codepoint set (65 'A', 66 'B')
        var primary = new[] { Rect(65, 10, 10), Rect(66, 12, 12) };
        var shadow = new[] { Rect(65, 10, 10), Rect(66, 12, 12) };
        var sources = new[]
        {
            new AtlasGroupBuilder.GroupSource("primary", primary),
            new AtlasGroupBuilder.GroupSource("shadow", shadow)
        };

        // Act
        var (_, placementsByTag) = AtlasGroupBuilder.Pack(new MaxRectsPacker(), sources, 256, 256);

        // Assert
        placementsByTag.Count.ShouldBe(2);
        placementsByTag["primary"].Count.ShouldBe(2);
        placementsByTag["shadow"].Count.ShouldBe(2);
        placementsByTag["primary"].Keys.ShouldBe(new[] { 65, 66 }, ignoreOrder: true);
        placementsByTag["shadow"].Keys.ShouldBe(new[] { 65, 66 }, ignoreOrder: true);

        // Placement.Id must be restored to the original id, not the composite id.
        placementsByTag["primary"][65].Id.ShouldBe(65);
        placementsByTag["shadow"][65].Id.ShouldBe(65);
    }

    // ---------------------------------------------------------------
    // 2. Placements for different sources never share a rect (page, x, y)
    // ---------------------------------------------------------------

    [Fact]
    public void Pack_TwoSourcesWithIdenticalRects_ProducesNonOverlappingPlacements()
    {
        // Arrange -- identical rect sets and sizes, so a naive shared pack (no id mangling)
        // would place primary[65] and shadow[65] at the very same (page,x,y) because the
        // packer would treat them as the same id/rect and only "see" one of them.
        var primary = new[] { Rect(65, 20, 20), Rect(66, 15, 15), Rect(67, 8, 8) };
        var shadow = new[] { Rect(65, 20, 20), Rect(66, 15, 15), Rect(67, 8, 8) };
        var sources = new[]
        {
            new AtlasGroupBuilder.GroupSource("primary", primary),
            new AtlasGroupBuilder.GroupSource("shadow", shadow)
        };

        // Act
        var (_, placementsByTag) = AtlasGroupBuilder.Pack(new MaxRectsPacker(), sources, 256, 256);

        // Assert -- every primary placement's (page,x,y) must differ from every shadow placement's.
        var primaryRects = placementsByTag["primary"].Values.Select(p => (p.PageIndex, p.X, p.Y)).ToHashSet();
        var shadowRects = placementsByTag["shadow"].Values.Select(p => (p.PageIndex, p.X, p.Y)).ToHashSet();

        primaryRects.Count.ShouldBe(3);
        shadowRects.Count.ShouldBe(3);
        primaryRects.Intersect(shadowRects).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------
    // 3. Shared PackResult sanity check -- page metadata comes from one combined pack
    // ---------------------------------------------------------------

    [Fact]
    public void Pack_TwoSources_ReturnsSharedPackResultCoveringAllRects()
    {
        // Arrange
        var primary = new[] { Rect(1, 30, 30), Rect(2, 30, 30) };
        var shadow = new[] { Rect(1, 30, 30), Rect(2, 30, 30) };
        var sources = new[]
        {
            new AtlasGroupBuilder.GroupSource("primary", primary),
            new AtlasGroupBuilder.GroupSource("shadow", shadow)
        };

        // Act
        var (shared, placementsByTag) = AtlasGroupBuilder.Pack(new MaxRectsPacker(), sources, 128, 128);

        // Assert -- total placements across both tags equals total rects submitted.
        var totalPlacements = placementsByTag.Values.Sum(d => d.Count);
        totalPlacements.ShouldBe(4);
        shared.PageWidth.ShouldBe(128);
        shared.PageHeight.ShouldBe(128);
        shared.PageCount.ShouldBeGreaterThanOrEqualTo(1);
        shared.Placements.Count.ShouldBe(4);
    }

    // ---------------------------------------------------------------
    // 4. Id overflowing the composite-id mask throws
    // ---------------------------------------------------------------

    [Fact]
    public void Pack_IdExceedingCompositeIdMask_Throws()
    {
        // Arrange -- 0x1000000 (16,777,216) overflows the 24-bit mask (max 0xFFFFFF).
        var primary = new[] { Rect(0x1000000, 10, 10) };
        var sources = new[] { new AtlasGroupBuilder.GroupSource("primary", primary) };

        // Act / Assert
        Should.Throw<ArgumentOutOfRangeException>(() => AtlasGroupBuilder.Pack(new MaxRectsPacker(), sources, 256, 256));
    }
}
