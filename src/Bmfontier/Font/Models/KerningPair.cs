namespace Bmfontier.Font.Models;

/// <summary>
/// A kerning pair adjustment in font units.
/// Callers scale: <c>XAdvanceAdjustment * targetSize / unitsPerEm</c>.
/// </summary>
public readonly record struct KerningPair(int LeftCodepoint, int RightCodepoint, int XAdvanceAdjustment);
