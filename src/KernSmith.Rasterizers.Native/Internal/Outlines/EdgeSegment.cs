namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// A directed straight segment in bitmap pixel space (Y down), ready for the scanline
/// rasterizer. Direction is significant: it carries the contour's winding, which the
/// signed-area coverage pass uses as the sign of its contribution.
/// </summary>
internal readonly record struct EdgeSegment(float X0, float Y0, float X1, float Y1);
