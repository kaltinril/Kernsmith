namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>
/// The part of a <see cref="DecodedGlyph"/> that places its box, without the outline: see
/// <see cref="IOutlineDecoder.TryDecodeBounds"/>.
/// </summary>
/// <param name="AdvanceWidth">The horizontal advance, in font units.</param>
/// <param name="IsEmpty">True when the outline has nothing to draw.</param>
/// <param name="DeclaredBounds">The declared box of a non-empty outline, in font units.</param>
internal readonly record struct DecodedBounds(float AdvanceWidth, bool IsEmpty, OutlineBounds DeclaredBounds);
