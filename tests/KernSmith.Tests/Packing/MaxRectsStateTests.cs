using KernSmith.Atlas;
using Shouldly;

namespace KernSmith.Tests.Packing;

/// <summary>
/// <see cref="MaxRectsState"/> unit tests — the grow-seam invariant: free space that
/// spans the boundary between the old page area and the grown strips must stay
/// placeable, exactly as if the state had been rebuilt at the new size.
/// </summary>
public sealed class MaxRectsStateTests
{
    [Fact]
    public void Grow_FreeStripAtOldRightEdge_PlacesSeamSpanningRect()
    {
        // 256x256 page with (0,0,200,256) occupied leaves a free right-edge strip
        // (200,0,56,256). After growing to 512x512, 312 contiguous pixels are free
        // from x=200 — a 300x50 rect must place there, not report no-fit.
        var state = new MaxRectsState(256, 256, 1);
        state.Subtract(new OccupiedRect(0, 0, 0, 200, 256));

        state.Grow(512, 512);

        var placement = state.TryPlace(new GlyphRect(1, 300, 50));
        placement.ShouldNotBeNull("free space spanning the grow seam must be placeable");
        placement.Value.X.ShouldBe(200);
        placement.Value.Y.ShouldBe(0);
    }

    [Fact]
    public void Grow_FreeStripAtOldBottomEdge_PlacesSeamSpanningRect()
    {
        // Same seam bug on the vertical axis: (0,0,256,200) occupied leaves a free
        // bottom-edge strip (0,200,256,56); after growing, a 50x300 rect spans the seam.
        var state = new MaxRectsState(256, 256, 1);
        state.Subtract(new OccupiedRect(0, 0, 0, 256, 200));

        state.Grow(512, 512);

        var placement = state.TryPlace(new GlyphRect(1, 50, 300));
        placement.ShouldNotBeNull("free space spanning the grow seam must be placeable");
        placement.Value.X.ShouldBe(0);
        placement.Value.Y.ShouldBe(200);
    }

    [Fact]
    public void Grow_ThenPlace_EqualsFreshStateWithSameSubtracts()
    {
        // Live-vs-rebuild invariant: a grown state must place exactly like a fresh
        // state seeded at the new size with the same occupancy subtracted.
        var occupied = new OccupiedRect(0, 0, 0, 200, 256);

        var grown = new MaxRectsState(256, 256, 1);
        grown.Subtract(occupied);
        grown.Grow(512, 512);

        var fresh = new MaxRectsState(512, 512, 1);
        fresh.Subtract(occupied);

        var rects = new[] { new GlyphRect(1, 300, 50), new GlyphRect(2, 400, 100) };
        foreach (var rect in rects)
        {
            var fromGrown = grown.TryPlace(rect);
            var fromFresh = fresh.TryPlace(rect);
            fromFresh.ShouldNotBeNull();
            fromGrown.ShouldBe(fromFresh, $"rect {rect.Id} placed differently after Grow than after a fresh rebuild");
        }
    }

    [Fact]
    public void Grow_SeamSpanningRect_FitsAfterSingleGrow()
    {
        // A rect that fits in the seam-spanning space after one grow must not force
        // a second (wasteful) grow.
        var state = new MaxRectsState(256, 256, 1);
        state.Subtract(new OccupiedRect(0, 0, 0, 200, 256));

        state.Grow(512, 512);
        state.TryPlace(new GlyphRect(1, 300, 50)).ShouldNotBeNull(
            "a single grow must expose the seam-spanning free space");

        state.PageWidth.ShouldBe(512);
        state.PageHeight.ShouldBe(512);
    }
}
