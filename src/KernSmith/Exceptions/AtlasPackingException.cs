namespace KernSmith;

/// <summary>
/// Thrown when glyphs don't fit in the texture atlas. Try a larger max texture size.
/// </summary>
public class AtlasPackingException : BmFontException
{
    /// <summary>Creates a packing error with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public AtlasPackingException(string message) : base(message) { }

    /// <summary>Creates a packing error with a message and the original exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="inner">The underlying exception.</param>
    public AtlasPackingException(string message, Exception inner) : base(message, inner) { }

    /// <summary>Creates a packing error for a glyph that's too big for the texture.</summary>
    /// <param name="glyphWidth">Glyph width in pixels.</param>
    /// <param name="glyphHeight">Glyph height in pixels.</param>
    /// <param name="maxTextureSize">Max allowed texture dimension in pixels.</param>
    public AtlasPackingException(int glyphWidth, int glyphHeight, int maxTextureSize)
        : base($"Glyph of size {glyphWidth}x{glyphHeight} exceeds maximum texture size of {maxTextureSize}")
    {
        GlyphWidth = glyphWidth;
        GlyphHeight = glyphHeight;
        MaxTextureSize = maxTextureSize;
    }

    /// <summary>Width of the glyph that didn't fit, if known.</summary>
    public int? GlyphWidth { get; }

    /// <summary>Height of the glyph that didn't fit, if known.</summary>
    public int? GlyphHeight { get; }

    /// <summary>Max texture size that was exceeded, if known.</summary>
    public int? MaxTextureSize { get; }
}
