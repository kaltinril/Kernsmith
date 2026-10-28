namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>Chooses the <see cref="IOutlineDecoder"/> for a face's outline format.</summary>
internal static class OutlineDecoders
{
    /// <summary>
    /// The decoder for <paramref name="face"/>, or null when its outline format has none yet —
    /// the rasterizer rejects such a face at <c>LoadFont</c>.
    /// </summary>
    public static IOutlineDecoder? ForFace(NativeFontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);

        if (face.HasGlyfOutlines)
            return new TrueTypeOutlineDecoder(face);

        return null;
    }
}
