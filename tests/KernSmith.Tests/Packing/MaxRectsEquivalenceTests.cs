using KernSmith.Atlas;
using Shouldly;
using Rect = KernSmith.Atlas.MaxRectsState.Rect;

namespace KernSmith.Tests.Packing;

/// <summary>
/// Holds the production MaxRects geometry to the frozen <see cref="ReferenceMaxRects"/>
/// oracle over randomized workloads: every placement and the full free list (contents
/// and order) must match after every operation, so packing output is bit-identical.
/// </summary>
public sealed class MaxRectsEquivalenceTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void TryPlaceInPage_RandomGlyphs_MatchesReferenceFreeListAfterEveryPlacement(int seed)
    {
        var rng = new Random(seed);
        for (var trial = 0; trial < 20; trial++)
        {
            var pageW = rng.Next(16, 300);
            var pageH = rng.Next(16, 300);
            var actual = new List<Rect> { new(0, 0, pageW, pageH) };
            var expected = new List<Rect> { new(0, 0, pageW, pageH) };

            // Small size alphabets force many equal-size ties and duplicate free rects.
            var maxSide = rng.Next(2) == 0 ? 6 : 40;
            for (var n = 0; n < 400; n++)
            {
                var w = rng.Next(1, maxSide);
                var h = rng.Next(1, maxSide);
                var a = MaxRectsState.TryPlaceInPage(actual, w, h);
                var e = ReferenceMaxRects.TryPlaceInPage(expected, w, h);
                a.ShouldBe(e);
                actual.ShouldBe(expected);
                if (e == null && rng.Next(4) == 0)
                    break;
            }
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public void MaxRectsState_RandomSubtractPlaceGrow_MatchesReference(int seed)
    {
        var rng = new Random(seed);
        for (var trial = 0; trial < 15; trial++)
        {
            var w = rng.Next(32, 200);
            var h = rng.Next(32, 200);
            var state = new MaxRectsState(w, h, 1);
            var reference = new List<List<Rect>> { new() { new(0, 0, w, h) } };

            for (var op = 0; op < 300; op++)
            {
                switch (rng.Next(10))
                {
                    case 0 or 1 or 2:
                        {
                            // Occupied rects may overlap each other and stick out of the page.
                            var page = rng.Next(reference.Count);
                            var r = new OccupiedRect(page, rng.Next(-4, w), rng.Next(-4, h),
                                rng.Next(1, 30), rng.Next(1, 30));
                            state.Subtract(r);
                            ReferenceMaxRects.Subtract(reference[page], r);
                            break;
                        }
                    case 3:
                        {
                            var nw = w + rng.Next(0, 40);
                            var nh = h + rng.Next(0, 40);
                            state.Grow(nw, nh);
                            foreach (var page in reference)
                                ReferenceMaxRects.Grow(page, w, h, nw, nh);
                            w = nw;
                            h = nh;
                            break;
                        }
                    case 4 when reference.Count < 4:
                        state.AddPage();
                        reference.Add(new List<Rect> { new(0, 0, w, h) });
                        break;
                    default:
                        {
                            var glyph = new GlyphRect(op, rng.Next(1, 24), rng.Next(1, 24));
                            var placed = state.TryPlace(glyph);
                            GlyphPlacement? expected = null;
                            for (var p = 0; p < reference.Count; p++)
                            {
                                var pos = ReferenceMaxRects.TryPlaceInPage(reference[p], glyph.Width, glyph.Height);
                                if (pos.HasValue)
                                {
                                    expected = new GlyphPlacement(glyph.Id, p, pos.Value.X, pos.Value.Y);
                                    break;
                                }
                            }
                            placed.ShouldBe(expected);
                            break;
                        }
                }

                state.PageCount.ShouldBe(reference.Count);
                for (var p = 0; p < reference.Count; p++)
                    state.GetFreeRects(p).ShouldBe(reference[p]);
            }
        }
    }

    [Fact]
    public void Pack_RandomGlyphSets_MatchesRecordedBaseline()
    {
        // Fingerprint of MaxRectsPacker.Pack/PackInto placements over many random sets,
        // recorded from the unoptimised full-prune implementation.
        var packer = new MaxRectsPacker();
        var rng = new Random(20261029);
        var hash = 14695981039346656037UL;

        void Mix(int v)
        {
            hash ^= (uint)v;
            hash *= 1099511628211UL;
        }

        for (var set = 0; set < 60; set++)
        {
            var count = rng.Next(1, 250);
            var maxSide = rng.Next(4, 48);
            var glyphs = new List<GlyphRect>(count);
            for (var i = 0; i < count; i++)
                glyphs.Add(new GlyphRect(i, rng.Next(1, maxSide), rng.Next(1, maxSide)));

            var size = rng.Next(64, 257);
            var result = packer.Pack(glyphs, size, size);
            Mix(result.PageCount);
            foreach (var p in result.Placements)
            {
                Mix(p.Id);
                Mix(p.PageIndex);
                Mix(p.X);
                Mix(p.Y);
            }

            var occupied = result.Placements
                .Take(result.Placements.Count / 2)
                .Select(p => new OccupiedRect(p.PageIndex, p.X, p.Y, glyphs[p.Id].Width, glyphs[p.Id].Height))
                .ToList();
            var extra = glyphs.Take(count / 3).Select(g => g with { Id = g.Id + 10000 }).ToList();
            var into = packer.PackInto(extra, occupied, result.PageCount, size, size);
            Mix(into.PageCount);
            foreach (var p in into.Placements)
            {
                Mix(p.Id);
                Mix(p.PageIndex);
                Mix(p.X);
                Mix(p.Y);
            }
        }

        hash.ShouldBe(1011169099166977608UL);
    }
}
