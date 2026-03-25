using KernSmith.Atlas;
using Shouldly;

namespace KernSmith.Tests.Packing;

public sealed class SkylinePackerTests
{
    private readonly SkylinePacker _packer = new();

    [Fact]
    public void Pack_SingleGlyph_FitsOnOnePage()
    {
        // Arrange
        var glyphs = new List<GlyphRect> { new(Id: 1, Width: 10, Height: 10) };

        // Act
        var result = _packer.Pack(glyphs, 256, 256);

        // Assert
        result.PageCount.ShouldBe(1);
        result.Placements.Count.ShouldBe(1);
        result.Placements[0].X.ShouldBe(0);
        result.Placements[0].Y.ShouldBe(0);
        result.Placements[0].PageIndex.ShouldBe(0);
    }

    [Fact]
    public void Pack_MultipleGlyphs_NoOverlap()
    {
        // Arrange
        var glyphs = new List<GlyphRect>();
        for (var i = 0; i < 10; i++)
        {
            glyphs.Add(new GlyphRect(Id: i, Width: 20 + i * 5, Height: 15 + i * 3));
        }

        // Act
        var result = _packer.Pack(glyphs, 256, 256);

        // Assert — verify no placements on the same page overlap
        var glyphLookup = glyphs.ToDictionary(g => g.Id);

        for (var i = 0; i < result.Placements.Count; i++)
        {
            for (var j = i + 1; j < result.Placements.Count; j++)
            {
                var a = result.Placements[i];
                var b = result.Placements[j];
                var aRect = glyphLookup[a.Id];
                var bRect = glyphLookup[b.Id];

                Overlaps(a, aRect, b, bRect).ShouldBeFalse(
                    $"glyph {a.Id} at ({a.X},{a.Y}) size ({aRect.Width}x{aRect.Height}) " +
                    $"overlaps glyph {b.Id} at ({b.X},{b.Y}) size ({bRect.Width}x{bRect.Height}) on page {a.PageIndex}");
            }
        }
    }

    [Fact]
    public void Pack_FillsMultiplePages()
    {
        // Arrange — 10 glyphs each 100x100 into 256x256 (only ~6 fit per page)
        var glyphs = new List<GlyphRect>();
        for (var i = 0; i < 10; i++)
        {
            glyphs.Add(new GlyphRect(Id: i, Width: 100, Height: 100));
        }

        // Act
        var result = _packer.Pack(glyphs, 256, 256);

        // Assert
        result.PageCount.ShouldBeGreaterThan(1);
        result.Placements.Count.ShouldBe(10);
    }

    [Fact]
    public void Pack_EmptyInput_ReturnsEmptyResult()
    {
        // Arrange
        var glyphs = new List<GlyphRect>();

        // Act
        var result = _packer.Pack(glyphs, 256, 256);

        // Assert
        result.Placements.ShouldBeEmpty();
        result.PageCount.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Pack_AllPlacementsWithinPageBounds()
    {
        // Arrange — 20 glyphs of varying sizes
        var glyphs = new List<GlyphRect>();
        for (var i = 0; i < 20; i++)
        {
            glyphs.Add(new GlyphRect(Id: i, Width: 10 + (i * 7 % 40), Height: 8 + (i * 11 % 35)));
        }

        const int pageWidth = 256;
        const int pageHeight = 256;

        // Act
        var result = _packer.Pack(glyphs, pageWidth, pageHeight);

        // Assert
        var glyphLookup = glyphs.ToDictionary(g => g.Id);
        foreach (var placement in result.Placements)
        {
            var glyph = glyphLookup[placement.Id];
            placement.X.ShouldBeGreaterThanOrEqualTo(0,
                $"glyph {placement.Id} X should be non-negative");
            placement.Y.ShouldBeGreaterThanOrEqualTo(0,
                $"glyph {placement.Id} Y should be non-negative");
            (placement.X + glyph.Width).ShouldBeLessThanOrEqualTo(pageWidth,
                $"glyph {placement.Id} right edge should be within page width");
            (placement.Y + glyph.Height).ShouldBeLessThanOrEqualTo(pageHeight,
                $"glyph {placement.Id} bottom edge should be within page height");
        }
    }

    [Fact]
    public void Pack_PreservesGlyphIds()
    {
        // Arrange
        var glyphs = new List<GlyphRect>
        {
            new(Id: 42, Width: 20, Height: 20),
            new(Id: 99, Width: 30, Height: 15),
            new(Id: 7, Width: 10, Height: 25),
            new(Id: 200, Width: 40, Height: 40),
            new(Id: 1, Width: 15, Height: 15),
        };

        // Act
        var result = _packer.Pack(glyphs, 256, 256);

        // Assert
        var inputIds = glyphs.Select(g => g.Id).OrderBy(id => id).ToList();
        var outputIds = result.Placements.Select(p => p.Id).OrderBy(id => id).ToList();

        outputIds.ShouldBe(inputIds);
        result.Placements.Select(p => p.Id).ShouldBeUnique();
    }

    private static bool Overlaps(GlyphPlacement a, GlyphRect aRect, GlyphPlacement b, GlyphRect bRect)
    {
        if (a.PageIndex != b.PageIndex) return false;
        return a.X < b.X + bRect.Width && a.X + aRect.Width > b.X &&
               a.Y < b.Y + bRect.Height && a.Y + aRect.Height > b.Y;
    }
}
