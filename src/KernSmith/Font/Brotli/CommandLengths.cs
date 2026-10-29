namespace KernSmith.Font.Brotli;

/// <summary>
/// What an insert-and-copy command symbol (RFC 7932 section 5) says about its two lengths: each
/// is a base plus that many extra bits read after the symbol.
/// </summary>
/// <param name="InsertBase">The insert length before its extra bits.</param>
/// <param name="InsertExtraBits">Extra bits of insert length (0..24).</param>
/// <param name="CopyBase">The copy length before its extra bits.</param>
/// <param name="CopyExtraBits">Extra bits of copy length (0..24).</param>
/// <param name="ImplicitDistance">True for the first two 64-symbol cells, whose copy reuses
/// the last distance without reading a distance code.</param>
internal readonly record struct CommandLengths(
    int InsertBase, int InsertExtraBits, int CopyBase, int CopyExtraBits, bool ImplicitDistance);
