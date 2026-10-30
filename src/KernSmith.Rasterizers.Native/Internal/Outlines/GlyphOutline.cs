namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>The kind of pen movement an <see cref="OutlineCommand"/> describes.</summary>
internal enum OutlineCommandType
{
    /// <summary>Lifts the pen and opens a new contour at the command's endpoint.</summary>
    MoveTo,

    /// <summary>A straight segment from the current point to the command's endpoint.</summary>
    LineTo,

    /// <summary>A cubic Bezier from the current point through both controls to the endpoint.</summary>
    CubicTo,

    /// <summary>
    /// Closes the current contour. The endpoint repeats the contour's start point, so a
    /// consumer can emit the closing straight segment without tracking the start itself.
    /// </summary>
    Close,
}

/// <summary>
/// One pen movement in a glyph outline, in font design units. Control points are only
/// meaningful for <see cref="OutlineCommandType.CubicTo"/> and are zero otherwise.
/// </summary>
internal readonly record struct OutlineCommand(
    OutlineCommandType Type,
    float EndX,
    float EndY,
    float Ctrl1X,
    float Ctrl1Y,
    float Ctrl2X,
    float Ctrl2Y)
{
    /// <summary>Opens a contour at the given point.</summary>
    public static OutlineCommand Move(float x, float y) =>
        new(OutlineCommandType.MoveTo, x, y, 0, 0, 0, 0);

    /// <summary>A straight segment ending at the given point.</summary>
    public static OutlineCommand Line(float x, float y) =>
        new(OutlineCommandType.LineTo, x, y, 0, 0, 0, 0);

    /// <summary>A cubic Bezier with the given controls, ending at the given point.</summary>
    public static OutlineCommand Cubic(float c1x, float c1y, float c2x, float c2y, float x, float y) =>
        new(OutlineCommandType.CubicTo, x, y, c1x, c1y, c2x, c2y);

    /// <summary>Closes a contour that started at the given point.</summary>
    public static OutlineCommand CloseAt(float x, float y) =>
        new(OutlineCommandType.Close, x, y, 0, 0, 0, 0);
}

/// <summary>
/// A glyph outline normalized into pen commands, still in font design units. Scaling and the
/// Y-axis flip are applied at flattening time (see <see cref="OutlineFlattener"/>), so one
/// extracted outline can be rendered at any pixel size.
/// </summary>
internal sealed class GlyphOutline
{
    /// <summary>An outline with no drawable contours, such as a space.</summary>
    public static readonly GlyphOutline Empty = new([], 0, 0, 0, 0);

    /// <summary>Creates an outline over the supplied commands and bounding box.</summary>
    public GlyphOutline(OutlineCommand[] commands, float xMin, float yMin, float xMax, float yMax)
    {
        Commands = commands;
        XMin = xMin;
        YMin = yMin;
        XMax = xMax;
        YMax = yMax;
    }

    /// <summary>
    /// Creates an outline over the supplied commands, bounding it by its control hull. This is
    /// how outline transforms build their result.
    /// </summary>
    public static GlyphOutline FromCommands(OutlineCommand[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Length == 0)
            return Empty;

        var (xMin, yMin, xMax, yMax) = ControlHull(commands);
        return new GlyphOutline(commands, xMin, yMin, xMax, yMax);
    }

    /// <summary>The pen commands, contour by contour, each opened by a
    /// <see cref="OutlineCommandType.MoveTo"/> and terminated by a
    /// <see cref="OutlineCommandType.Close"/>.</summary>
    public OutlineCommand[] Commands { get; }

    /// <summary>Left edge of the outline's control hull, in font units.</summary>
    public float XMin { get; }

    /// <summary>Bottom edge of the outline's control hull, in font units.</summary>
    public float YMin { get; }

    /// <summary>Right edge of the outline's control hull, in font units.</summary>
    public float XMax { get; }

    /// <summary>Top edge of the outline's control hull, in font units.</summary>
    public float YMax { get; }

    /// <summary>True when the outline has nothing to draw.</summary>
    public bool IsEmpty => Commands.Length == 0;

    /// <summary>
    /// Bounds every point a command touches, control points included. A cubic never leaves
    /// its control hull, so this is conservative — it can only over-estimate, never clip.
    /// </summary>
    private static (float XMin, float YMin, float XMax, float YMax) ControlHull(OutlineCommand[] commands)
    {
        float xMin = float.MaxValue, yMin = float.MaxValue;
        float xMax = float.MinValue, yMax = float.MinValue;

        foreach (var command in commands)
        {
            Include(command.EndX, command.EndY);

            if (command.Type != OutlineCommandType.CubicTo)
                continue;

            Include(command.Ctrl1X, command.Ctrl1Y);
            Include(command.Ctrl2X, command.Ctrl2Y);
        }

        return (xMin, yMin, xMax, yMax);

        void Include(float x, float y)
        {
            if (x < xMin) xMin = x;
            if (y < yMin) yMin = y;
            if (x > xMax) xMax = x;
            if (y > yMax) yMax = y;
        }
    }
}
