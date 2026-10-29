using KernSmith.Rasterizers.Native.Internal.Variations;

namespace KernSmith.Rasterizers.Native.Internal.Outlines;

/// <summary>Chooses the <see cref="IOutlineDecoder"/> for a face's outline format.</summary>
internal static class OutlineDecoders
{
    /// <summary>
    /// The decoder for <paramref name="face"/>, or null when its outline format has none yet —
    /// the rasterizer rejects such a face at <c>LoadFont</c>.
    /// </summary>
    /// <param name="face">The loaded face.</param>
    /// <param name="variations">The face's variation state, for a variable font.</param>
    public static IOutlineDecoder? ForFace(NativeFontFace face, FontVariations? variations = null)
    {
        ArgumentNullException.ThrowIfNull(face);

        if (face.HasGlyfOutlines)
            return new TrueTypeOutlineDecoder(face, variations);

        return null;
    }
}
