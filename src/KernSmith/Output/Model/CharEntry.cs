namespace KernSmith.Output.Model;

/// <summary>
/// BMFont char entry — a single glyph's atlas position and rendering metrics.
/// </summary>
public sealed record CharEntry(
    int Id,
    int X,
    int Y,
    int Width,
    int Height,
    int XOffset,
    int YOffset,
    int XAdvance,
    int Page,
    int Channel = 15);
