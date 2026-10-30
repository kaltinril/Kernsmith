namespace KernSmith.Rasterizers.Native.Internal.Color;

/// <summary>A color as premultiplied, linear-light RGBA in 0..1 — the form COLR version 1 interpolates and composites in.</summary>
internal readonly record struct LinearColor(float R, float G, float B, float A)
{
    /// <summary>Linear interpolation between two premultiplied colors.</summary>
    public static LinearColor Lerp(LinearColor a, LinearColor b, float t) => new(
        a.R + ((b.R - a.R) * t),
        a.G + ((b.G - a.G) * t),
        a.B + ((b.B - a.B) * t),
        a.A + ((b.A - a.A) * t));
}

/// <summary>The COLR CompositeMode enumeration (W3C Compositing and Blending Level 1 operators).</summary>
internal enum CompositeMode
{
    Clear = 0,
    Source = 1,
    Destination = 2,
    SourceOver = 3,
    DestinationOver = 4,
    SourceIn = 5,
    DestinationIn = 6,
    SourceOut = 7,
    DestinationOut = 8,
    SourceAtop = 9,
    DestinationAtop = 10,
    Xor = 11,
    Plus = 12,
    Screen = 13,
    Overlay = 14,
    Darken = 15,
    Lighten = 16,
    ColorDodge = 17,
    ColorBurn = 18,
    HardLight = 19,
    SoftLight = 20,
    Difference = 21,
    Exclusion = 22,
    Multiply = 23,
    Hue = 24,
    Saturation = 25,
    Color = 26,
    Luminosity = 27,
}

/// <summary>The COLR color line Extend enumeration.</summary>
internal enum ColorLineExtend
{
    Pad = 0,
    Repeat = 1,
    Reflect = 2,
}

/// <summary>One color stop with its palette color resolved.</summary>
internal readonly record struct ColorStop(float Offset, LinearColor Color);

/// <summary>
/// A resolved COLR version 1 paint graph node, in font units, with variation deltas, palette
/// colors and <c>PaintColrGlyph</c> references already applied by <see cref="ColrPaintReader"/>.
/// The various transform formats are all reduced to <see cref="TransformPaint"/>.
/// </summary>
internal abstract class ColrPaint
{
}

/// <summary>PaintColrLayers: children composited bottom first with source-over.</summary>
internal sealed class LayersPaint(ColrPaint[] layers) : ColrPaint
{
    public ColrPaint[] Layers { get; } = layers;
}

/// <summary>PaintSolid / PaintVarSolid.</summary>
internal sealed class SolidPaint(LinearColor color) : ColrPaint
{
    public LinearColor Color { get; } = color;
}

/// <summary>A gradient: its color line, sorted by stop offset (stable, so font order breaks ties).</summary>
internal abstract class GradientPaint(ColorStop[] stops, ColorLineExtend extend) : ColrPaint
{
    public ColorStop[] Stops { get; } = stops;

    public ColorLineExtend Extend { get; } = extend;
}

/// <summary>PaintLinearGradient / PaintVarLinearGradient.</summary>
internal sealed class LinearGradientPaint(ColorStop[] stops, ColorLineExtend extend,
    double x0, double y0, double x1, double y1, double x2, double y2) : GradientPaint(stops, extend)
{
    public double X0 { get; } = x0;
    public double Y0 { get; } = y0;
    public double X1 { get; } = x1;
    public double Y1 { get; } = y1;
    public double X2 { get; } = x2;
    public double Y2 { get; } = y2;
}

/// <summary>PaintRadialGradient / PaintVarRadialGradient.</summary>
internal sealed class RadialGradientPaint(ColorStop[] stops, ColorLineExtend extend,
    double x0, double y0, double r0, double x1, double y1, double r1) : GradientPaint(stops, extend)
{
    public double X0 { get; } = x0;
    public double Y0 { get; } = y0;
    public double R0 { get; } = r0;
    public double X1 { get; } = x1;
    public double Y1 { get; } = y1;
    public double R1 { get; } = r1;
}

/// <summary>PaintSweepGradient / PaintVarSweepGradient; angles in counter-clockwise degrees.</summary>
internal sealed class SweepGradientPaint(ColorStop[] stops, ColorLineExtend extend,
    double centerX, double centerY, double startDegrees, double endDegrees) : GradientPaint(stops, extend)
{
    public double CenterX { get; } = centerX;
    public double CenterY { get; } = centerY;
    public double StartDegrees { get; } = startDegrees;
    public double EndDegrees { get; } = endDegrees;
}

/// <summary>PaintGlyph: the child, clipped to a glyph outline.</summary>
internal sealed class GlyphClipPaint(int glyphIndex, ColrPaint child) : ColrPaint
{
    public int GlyphIndex { get; } = glyphIndex;

    public ColrPaint Child { get; } = child;
}

/// <summary>The child, clipped to a rectangle in font units — a glyph's clip box.</summary>
internal sealed class RectClipPaint(double xMin, double yMin, double xMax, double yMax, ColrPaint child) : ColrPaint
{
    public double XMin { get; } = xMin;
    public double YMin { get; } = yMin;
    public double XMax { get; } = xMax;
    public double YMax { get; } = yMax;

    public ColrPaint Child { get; } = child;
}

/// <summary>PaintTransform, PaintTranslate, PaintScale*, PaintRotate*, PaintSkew* (and their Var forms).</summary>
internal sealed class TransformPaint(Affine2D transform, ColrPaint child) : ColrPaint
{
    public Affine2D Transform { get; } = transform;

    public ColrPaint Child { get; } = child;
}

/// <summary>PaintComposite. A null child is an ignored (invalid) sub-graph: transparent.</summary>
internal sealed class CompositePaint(CompositeMode mode, ColrPaint? source, ColrPaint? backdrop) : ColrPaint
{
    public CompositeMode Mode { get; } = mode;

    public ColrPaint? Source { get; } = source;

    public ColrPaint? Backdrop { get; } = backdrop;
}

/// <summary>
/// A base glyph's resolved paint graph.
/// </summary>
/// <param name="Root">The root paint; null when the whole graph was invalid (renders nothing).</param>
/// <param name="ClipBox">The glyph's clip box in font units, or null when it has none.</param>
internal sealed record ColrGlyphGraph(ColrPaint? Root, (double XMin, double YMin, double XMax, double YMax)? ClipBox);
