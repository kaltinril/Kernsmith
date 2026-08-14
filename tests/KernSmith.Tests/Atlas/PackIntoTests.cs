using KernSmith.Atlas;
using Shouldly;

namespace KernSmith.Tests.Atlas;

public sealed class PackIntoTests
{
    private readonly MaxRectsPacker _packer = new();

    // ---------------------------------------------------------------
    // MaxRectsPacker.PackInto
    // ---------------------------------------------------------------

    [Fact]
    public void PackInto_NewPlacements_NeverOverlapOccupied()
    {
        // Arrange — occupied rect at the origin; a naive fresh pack would place
        // the first glyph at (0,0), colliding with it.
        var occupied = new List<OccupiedRect> { new(PageIndex: 0, X: 0, Y: 0, Width: 100, Height: 100) };
        var glyphs = new List<GlyphRect> { new(Id: 1, Width: 50, Height: 50) };

        // Act
        var result = _packer.PackInto(glyphs, occupied, pageCount: 1, maxWidth: 256, maxHeight: 256);

        // Assert
        result.Placements.Count.ShouldBe(1);
        var p = result.Placements[0];
        OverlapsOccupied(p, glyphs[0], occupied[0]).ShouldBeFalse(
            $"glyph {p.Id} at ({p.X},{p.Y}) size 50x50 overlaps the occupied rect at (0,0) 100x100");
    }

    [Fact]
    public void PackInto_NewPlacements_NeverOverlapEachOther_AndStayInBounds()
    {
        // Arrange
        const int pageWidth = 256;
        const int pageHeight = 256;
        var occupied = new List<OccupiedRect>
        {
            new(PageIndex: 0, X: 0, Y: 0, Width: 120, Height: 80),
            new(PageIndex: 0, X: 120, Y: 0, Width: 60, Height: 40),
        };
        var glyphs = new List<GlyphRect>();
        for (var i = 0; i < 12; i++)
        {
            glyphs.Add(new GlyphRect(Id: i, Width: 15 + (i * 7 % 40), Height: 12 + (i * 11 % 35)));
        }

        // Act
        var result = _packer.PackInto(glyphs, occupied, pageCount: 1, maxWidth: pageWidth, maxHeight: pageHeight);

        // Assert — pairwise no overlap between new placements
        var glyphLookup = glyphs.ToDictionary(g => g.Id);
        for (var i = 0; i < result.Placements.Count; i++)
        {
            for (var j = i + 1; j < result.Placements.Count; j++)
            {
                var a = result.Placements[i];
                var b = result.Placements[j];
                Overlaps(a, glyphLookup[a.Id], b, glyphLookup[b.Id]).ShouldBeFalse(
                    $"glyph {a.Id} at ({a.X},{a.Y}) overlaps glyph {b.Id} at ({b.X},{b.Y}) on page {a.PageIndex}");
            }
        }

        // Assert — no overlap with occupied, and all in bounds
        foreach (var p in result.Placements)
        {
            var glyph = glyphLookup[p.Id];
            foreach (var occ in occupied)
            {
                OverlapsOccupied(p, glyph, occ).ShouldBeFalse(
                    $"glyph {p.Id} at ({p.X},{p.Y}) overlaps occupied rect at ({occ.X},{occ.Y})");
            }

            p.X.ShouldBeGreaterThanOrEqualTo(0);
            p.Y.ShouldBeGreaterThanOrEqualTo(0);
            (p.X + glyph.Width).ShouldBeLessThanOrEqualTo(pageWidth,
                $"glyph {p.Id} right edge should be within page width");
            (p.Y + glyph.Height).ShouldBeLessThanOrEqualTo(pageHeight,
                $"glyph {p.Id} bottom edge should be within page height");
        }
    }

    [Fact]
    public void PackInto_ScrambledOccupiedOrder_ProducesIdenticalPlacements()
    {
        // Arrange — same occupied rects in two different orders
        var occupiedA = new List<OccupiedRect>
        {
            new(0, 0, 0, 40, 40),
            new(0, 40, 0, 40, 40),
            new(0, 0, 40, 40, 40),
            new(0, 80, 0, 30, 60),
        };
        var occupiedB = new List<OccupiedRect> { occupiedA[3], occupiedA[1], occupiedA[0], occupiedA[2] };

        var glyphs = new List<GlyphRect>();
        for (var i = 0; i < 8; i++)
        {
            glyphs.Add(new GlyphRect(Id: i, Width: 10 + i * 3, Height: 10 + i * 2));
        }

        // Act
        var resultA = _packer.PackInto(glyphs, occupiedA, pageCount: 1, maxWidth: 128, maxHeight: 128);
        var resultB = _packer.PackInto(glyphs, occupiedB, pageCount: 1, maxWidth: 128, maxHeight: 128);

        // Assert — identical placements in identical order
        resultB.Placements.ShouldBe(resultA.Placements);
        resultB.PageCount.ShouldBe(resultA.PageCount);
    }

    [Fact]
    public void PackInto_EmptyOccupied_EqualsFreshPack()
    {
        // Arrange
        var glyphs = new List<GlyphRect>();
        for (var i = 0; i < 15; i++)
        {
            glyphs.Add(new GlyphRect(Id: i, Width: 12 + (i * 5 % 30), Height: 9 + (i * 13 % 25)));
        }

        // Act
        var packed = _packer.Pack(glyphs, 256, 256);
        var packedInto = _packer.PackInto(glyphs, [], pageCount: 1, maxWidth: 256, maxHeight: 256);

        // Assert — exact same placements as a fresh pack of the same rects
        packedInto.Placements.ShouldBe(packed.Placements);
        packedInto.PageCount.ShouldBe(packed.PageCount);
        packedInto.PageWidth.ShouldBe(packed.PageWidth);
        packedInto.PageHeight.ShouldBe(packed.PageHeight);
    }

    [Fact]
    public void PackInto_GlyphTooBigForRemainingSpace_AddsNewPage()
    {
        // Arrange — page 0 mostly occupied; an 80x80 glyph fits an empty page
        // but not the remaining 100x40 strip.
        var occupied = new List<OccupiedRect> { new(PageIndex: 0, X: 0, Y: 0, Width: 100, Height: 60) };
        var glyphs = new List<GlyphRect> { new(Id: 1, Width: 80, Height: 80) };

        // Act
        var result = _packer.PackInto(glyphs, occupied, pageCount: 1, maxWidth: 100, maxHeight: 100);

        // Assert
        result.PageCount.ShouldBe(2);
        result.Placements.Count.ShouldBe(1);
        result.Placements[0].PageIndex.ShouldBe(1);
        result.Placements[0].X.ShouldBe(0);
        result.Placements[0].Y.ShouldBe(0);
    }

    [Fact]
    public void PackInto_GlyphLargerThanEmptyPage_Throws()
    {
        // Arrange
        var glyphs = new List<GlyphRect> { new(Id: 1, Width: 300, Height: 300) };

        // Act & Assert
        Should.Throw<InvalidOperationException>(() =>
            _packer.PackInto(glyphs, [], pageCount: 1, maxWidth: 256, maxHeight: 256));
    }

    [Fact]
    public void PackInto_FntStyleOccupancy_NewGlyphLandsOutsideOccupiedTiles()
    {
        // Arrange — four occupied rects tiling the 64x64 top-left corner of a 128x128 page,
        // the way .fnt char entries tile a generated atlas.
        var occupied = new List<OccupiedRect>
        {
            new(0, 0, 0, 32, 32),
            new(0, 32, 0, 32, 32),
            new(0, 0, 32, 32, 32),
            new(0, 32, 32, 32, 32),
        };
        var glyphs = new List<GlyphRect> { new(Id: 81, Width: 32, Height: 32) };

        // Act
        var result = _packer.PackInto(glyphs, occupied, pageCount: 1, maxWidth: 128, maxHeight: 128);

        // Assert
        result.Placements.Count.ShouldBe(1);
        var p = result.Placements[0];
        p.PageIndex.ShouldBe(0);
        foreach (var occ in occupied)
        {
            OverlapsOccupied(p, glyphs[0], occ).ShouldBeFalse(
                $"glyph at ({p.X},{p.Y}) overlaps occupied tile at ({occ.X},{occ.Y})");
        }
        (p.X + 32).ShouldBeLessThanOrEqualTo(128);
        (p.Y + 32).ShouldBeLessThanOrEqualTo(128);
    }

    // ---------------------------------------------------------------
    // SkylinePacker.PackInto
    // ---------------------------------------------------------------

    [Fact]
    public void SkylinePacker_PackInto_ThrowsNotSupported()
    {
        // Arrange — skyline state cannot be reconstructed from occupied rects
        // (holes under the skyline are lost), so PackInto is unsupported.
        IAtlasPacker packer = new SkylinePacker();
        var glyphs = new List<GlyphRect> { new(Id: 1, Width: 10, Height: 10) };

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            packer.PackInto(glyphs, [], pageCount: 1, maxWidth: 256, maxHeight: 256));
    }

    // ---------------------------------------------------------------
    // MaxRectsState.Grow
    // ---------------------------------------------------------------

    [Fact]
    public void Grow_GlyphThatDidNotFit_FitsAfterGrow_WithoutOverlappingEarlierPlacements()
    {
        // Arrange — 64x64 page; a 50x50 glyph fills most of it, so a second
        // 50x50 glyph cannot fit until the page grows.
        var state = new MaxRectsState(pageWidth: 64, pageHeight: 64, initialPageCount: 1);
        var first = state.TryPlace(new GlyphRect(Id: 1, Width: 50, Height: 50));
        first.ShouldNotBeNull();

        var before = state.TryPlace(new GlyphRect(Id: 2, Width: 50, Height: 50));
        before.ShouldBeNull("a second 50x50 glyph should not fit a 64x64 page already holding one");

        // Act
        state.Grow(128, 128);
        var after = state.TryPlace(new GlyphRect(Id: 2, Width: 50, Height: 50));

        // Assert
        state.PageWidth.ShouldBe(128);
        state.PageHeight.ShouldBe(128);
        after.ShouldNotBeNull();
        Overlaps(first.Value, new GlyphRect(1, 50, 50), after.Value, new GlyphRect(2, 50, 50))
            .ShouldBeFalse("post-grow placement must not overlap the pre-grow placement");
        (after.Value.X + 50).ShouldBeLessThanOrEqualTo(128);
        (after.Value.Y + 50).ShouldBeLessThanOrEqualTo(128);
    }

    [Fact]
    public void Grow_Smaller_Throws()
    {
        // Arrange
        var state = new MaxRectsState(pageWidth: 64, pageHeight: 64, initialPageCount: 1);

        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => state.Grow(32, 128));
        Should.Throw<ArgumentOutOfRangeException>(() => state.Grow(128, 32));
    }

    private static bool Overlaps(GlyphPlacement a, GlyphRect aRect, GlyphPlacement b, GlyphRect bRect)
    {
        if (a.PageIndex != b.PageIndex) return false;
        return a.X < b.X + bRect.Width && a.X + aRect.Width > b.X &&
               a.Y < b.Y + bRect.Height && a.Y + aRect.Height > b.Y;
    }

    private static bool OverlapsOccupied(GlyphPlacement p, GlyphRect rect, OccupiedRect occ)
    {
        if (p.PageIndex != occ.PageIndex) return false;
        return p.X < occ.X + occ.Width && p.X + rect.Width > occ.X &&
               p.Y < occ.Y + occ.Height && p.Y + rect.Height > occ.Y;
    }
}
