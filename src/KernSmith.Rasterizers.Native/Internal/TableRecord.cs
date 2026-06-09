namespace KernSmith.Rasterizers.Native.Internal;

/// <summary>
/// A single entry in the SFNT table directory: the table's tag, checksum, and the
/// byte range (offset + length) where its data lives in the font file.
/// </summary>
internal readonly record struct TableRecord(string Tag, uint Checksum, uint Offset, uint Length);
