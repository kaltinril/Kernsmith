namespace KernSmith.Rasterizers.Native.Internal.Color;

/// <summary>
/// A 2D affine transform in the <c>COLR</c> Affine2x3 layout: a point (x, y) maps to
/// (xx·x + xy·y + dx, yx·x + yy·y + dy).
/// </summary>
internal readonly record struct Affine2D(double Xx, double Yx, double Xy, double Yy, double Dx, double Dy)
{
    /// <summary>The identity transform.</summary>
    public static readonly Affine2D Identity = new(1, 0, 0, 1, 0, 0);

    /// <summary>A translation.</summary>
    public static Affine2D Translation(double dx, double dy) => new(1, 0, 0, 1, dx, dy);

    /// <summary>A scale about the origin.</summary>
    public static Affine2D Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    /// <summary>A counter-clockwise rotation (Y up) about the origin.</summary>
    public static Affine2D Rotation(double radians)
    {
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new Affine2D(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>
    /// A skew about the origin, as the <c>COLR</c> PaintSkew tables define it:
    /// xy = −tan(x angle), yx = tan(y angle).
    /// </summary>
    public static Affine2D Skewing(double xRadians, double yRadians) =>
        new(1, Math.Tan(yRadians), -Math.Tan(xRadians), 1, 0, 0);

    /// <summary>Applies <paramref name="transform"/> about (<paramref name="cx"/>, <paramref name="cy"/>) instead of the origin.</summary>
    public static Affine2D AroundCenter(Affine2D transform, double cx, double cy) =>
        Translation(cx, cy) * transform * Translation(-cx, -cy);

    /// <summary>
    /// Composition: <c>a * b</c> applies <paramref name="b"/> first, then <paramref name="a"/>.
    /// </summary>
    public static Affine2D operator *(Affine2D a, Affine2D b) => new(
        Xx: (a.Xx * b.Xx) + (a.Xy * b.Yx),
        Yx: (a.Yx * b.Xx) + (a.Yy * b.Yx),
        Xy: (a.Xx * b.Xy) + (a.Xy * b.Yy),
        Yy: (a.Yx * b.Xy) + (a.Yy * b.Yy),
        Dx: (a.Xx * b.Dx) + (a.Xy * b.Dy) + a.Dx,
        Dy: (a.Yx * b.Dx) + (a.Yy * b.Dy) + a.Dy);

    /// <summary>The determinant of the linear part.</summary>
    public double Determinant => (Xx * Yy) - (Xy * Yx);

    /// <summary>Maps a point.</summary>
    public (double X, double Y) Apply(double x, double y) =>
        ((Xx * x) + (Xy * y) + Dx, (Yx * x) + (Yy * y) + Dy);

    /// <summary>The inverse transform; false when the transform collapses the plane (zero determinant).</summary>
    public bool TryInvert(out Affine2D inverse)
    {
        double det = Determinant;
        if (det.Equals(0d) || !double.IsFinite(det))
        {
            inverse = default;
            return false;
        }

        double xx = Yy / det;
        double yx = -Yx / det;
        double xy = -Xy / det;
        double yy = Xx / det;
        inverse = new Affine2D(xx, yx, xy, yy, -((xx * Dx) + (xy * Dy)), -((yx * Dx) + (yy * Dy)));
        return true;
    }
}
