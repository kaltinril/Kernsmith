namespace KernSmith.Font.Models;

/// <summary>
/// A kerning pair with the adjustment already scaled to pixel values.
/// Unlike <see cref="KerningPair"/> (which stores font design units requiring caller scaling),
/// this type is used by rasterizers that return pre-scaled pixel values (e.g., GDI's GetKerningPairsW).
/// </summary>
public readonly record struct ScaledKerningPair(int LeftCodepoint, int RightCodepoint, int Amount);
