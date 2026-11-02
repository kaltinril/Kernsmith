namespace KernSmith.Output.Model;

/// <summary>
/// One character's unrounded horizontal metrics, written to the extended metadata when
/// <see cref="FontGeneratorOptions.FractionalMetrics"/> is on (text: a <c>kernsmithchar</c> line
/// per character after the <c>kernsmith</c> line; XML: a <c>&lt;char&gt;</c> child of
/// <c>&lt;kernsmith&gt;</c>; binary: the <c>chars</c> array of block 6). Every value is in output
/// pixels, a multiple of 1/64 px, so it is exact in decimal and in a float.
/// </summary>
/// <param name="Id">The Unicode codepoint, as in the <c>char</c> entry.</param>
/// <param name="Advance">The pen advance before rounding: what <c>xadvance</c> is the rounding of
/// (the font's advance at this size, super sampling, synthetic width and bold, the global advance
/// adjustment and any effect's growth included), without hinting.</param>
/// <param name="Lsb">The left side bearing before rounding: the distance from the pen origin to the
/// left edge of the unhinted outline's control box. Unlike <c>xoffset</c> it carries no padding,
/// outline or shadow margin, distance-field spread or <c>ForceOffsetsToZero</c>.</param>
/// <param name="AdvanceDelta">What hinting added to the advance (hinted minus unhinted; where
/// present, <c>Advance + AdvanceDelta</c> is the hinted advance <c>xadvance</c> was rounded from),
/// or null when the glyph was not hinted or the backend does not report it (GDI, DirectWrite,
/// StbTrueType).</param>
/// <param name="LsbDelta">How far hinting moved the ink's left edge (hinted minus unhinted), or null
/// as for <paramref name="AdvanceDelta"/>.</param>
public readonly record struct FractionalCharMetrics(int Id, float Advance, float Lsb, float? AdvanceDelta = null, float? LsbDelta = null);
