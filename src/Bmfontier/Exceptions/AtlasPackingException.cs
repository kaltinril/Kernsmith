namespace Bmfontier;

public class AtlasPackingException : BmfontierException
{
    public AtlasPackingException(string message) : base(message) { }
    public AtlasPackingException(string message, Exception inner) : base(message, inner) { }

    public AtlasPackingException(int glyphWidth, int glyphHeight, int maxTextureSize)
        : base($"Glyph of size {glyphWidth}x{glyphHeight} exceeds maximum texture size of {maxTextureSize}")
    {
        GlyphWidth = glyphWidth;
        GlyphHeight = glyphHeight;
        MaxTextureSize = maxTextureSize;
    }

    public int? GlyphWidth { get; }
    public int? GlyphHeight { get; }
    public int? MaxTextureSize { get; }
}
