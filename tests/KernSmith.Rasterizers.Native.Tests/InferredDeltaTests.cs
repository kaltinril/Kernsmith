using KernSmith.Rasterizers.Native.Internal.Variations;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 171 — inferred deltas for points a <c>gvar</c> tuple does not reference (the IUP-like
/// step), per the "Inferred deltas for un-referenced point numbers" section of the 'gvar' chapter.
/// </summary>
public class InferredDeltaTests
{
    [Fact]
    public void SpecExample_InterpolatesXAndTakesTheNearerYDelta()
    {
        // P1 (245, 100) and P3 (305, 50) are referenced, P2 (260, 40) is not. In X, P2 lies a
        // quarter of the way from P1 to P3: (1 - 0.25) x 28 + 0.25 x -42 = 10.5. In Y it lies
        // below both, so it takes the delta of the lower one, P3.
        int[] xs = [245, 260, 305];
        int[] ys = [100, 40, 50];
        bool[] touched = [true, false, true];
        float[] dx = [28f, 0f, -42f];
        float[] dy = [-62f, 0f, -57f];

        InferredDeltas.InferContour(xs, dx, touched, 0, 2);
        InferredDeltas.InferContour(ys, dy, touched, 0, 2);

        dx.ShouldBe([28f, 10.5f, -42f]);
        dy.ShouldBe([-62f, -57f, -57f]);
    }

    [Fact]
    public void AboveBothNeighbours_TakesTheHigherOnesDelta()
    {
        int[] xs = [0, 500, 100];
        bool[] touched = [true, false, true];
        float[] dx = [3f, 0f, 7f];

        InferredDeltas.InferContour(xs, dx, touched, 0, 2);

        dx[1].ShouldBe(7f);
    }

    [Fact]
    public void OneReferencedPoint_ShiftsTheWholeContour()
    {
        int[] xs = [0, 50, 100, 50];
        bool[] touched = [false, false, true, false];
        float[] dx = [0f, 0f, 9f, 0f];

        InferredDeltas.InferContour(xs, dx, touched, 0, 3);

        dx.ShouldBe([9f, 9f, 9f, 9f]);
    }

    [Theory]
    [InlineData(4f, 4f, 4f)]  // equal neighbour coordinates and deltas: that delta
    [InlineData(4f, 6f, 0f)]  // equal coordinates, different deltas: zero
    public void EqualNeighbourCoordinates(float before, float after, float expected)
    {
        int[] xs = [10, 999, 10];
        bool[] touched = [true, false, true];
        float[] dx = [before, 0f, after];

        InferredDeltas.InferContour(xs, dx, touched, 0, 2);

        dx[1].ShouldBe(expected);
    }

    [Fact]
    public void Neighbours_WrapAroundTheContour()
    {
        // Point 0 has no referenced point before it, so its "preceding" neighbour is the
        // highest referenced point of the contour (3); point 4 likewise wraps to point 1.
        int[] xs = [50, 0, 999, 100, 75];
        bool[] touched = [false, true, false, true, false];
        float[] dx = [0f, 10f, 0f, 20f, 0f];

        InferredDeltas.InferContour(xs, dx, touched, 0, 4);

        dx[0].ShouldBe(15f);   // halfway between x=100 (20) and x=0 (10)
        dx[2].ShouldBe(20f);   // beyond both: the larger coordinate's delta
        dx[4].ShouldBe(17.5f); // between x=100 (20) and x=0 (10), a quarter from 100
    }

    [Fact]
    public void UntouchedContour_IsLeftAlone()
    {
        int[] xs = [0, 10, 20];
        bool[] touched = [false, false, false];
        float[] dx = [0f, 0f, 0f];

        InferredDeltas.InferContour(xs, dx, touched, 0, 2);

        dx.ShouldBe([0f, 0f, 0f]);
    }

    [Fact]
    public void OnlyTheGivenContourIsTouched()
    {
        // Two contours, [0..1] and [2..4]; inferring the second must not write into the first.
        int[] xs = [0, 0, 0, 50, 100];
        bool[] touched = [false, false, true, false, true];
        float[] dx = [0f, 0f, 2f, 0f, 4f];

        InferredDeltas.InferContour(xs, dx, touched, 2, 4);

        dx.ShouldBe([0f, 0f, 2f, 3f, 4f]);
    }
}
