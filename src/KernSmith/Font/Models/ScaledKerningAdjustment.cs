namespace KernSmith.Font.Models;

/// <summary>
/// A backend's correction to one pair's spacing, in pixels at the requested size, added to the
/// font's own kerning for that pair (if any) before the pair is rounded to the whole pixel the
/// <c>.fnt</c> format holds. Positive opens the pair. Reported by
/// <see cref="Rasterizer.IRasterizer.GetKerningAdjustments"/> when
/// <see cref="Rasterizer.RasterOptions.BakeHintingIntoKerning"/> is on: a hinter that moves a
/// glyph's side bearings to the pixel grid changes every gap that glyph takes part in, and the
/// pair's correction is what brings that gap back to the font's.
/// </summary>
/// <param name="LeftCodepoint">The first glyph of the pair.</param>
/// <param name="RightCodepoint">The second glyph of the pair.</param>
/// <param name="Amount">The correction in pixels, positive to open the pair.</param>
public readonly record struct ScaledKerningAdjustment(int LeftCodepoint, int RightCodepoint, float Amount);
