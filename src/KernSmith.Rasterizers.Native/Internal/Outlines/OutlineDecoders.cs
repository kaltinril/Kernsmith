namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>Chooses the <see cref="IOutlineDecoder"/> for a face's outline format.</summary>
internal static class OutlineDecoders
{
    /// <summary>
    /// The decoder for <paramref name="face"/>: TrueType for a <c>glyf</c> face, CFF for a
    /// <c>CFF </c> or <c>CFF2</c> face. CFF tables are parsed here, so a malformed one fails the
    /// load rather than every glyph.
    /// </summary>
    /// <exception cref="FontFormatException">If the face's outline table is malformed.</exception>
    public static IOutlineDecoder ForFace(NativeFontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);

        if (face.HasGlyfOutlines)
            return new TrueTypeOutlineDecoder(face);

        return new CffOutlineDecoder(face);
    }
}
