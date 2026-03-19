namespace Bmfontier;

public class RasterizationException : BmfontierException
{
    public RasterizationException(string message) : base(message) { }
    public RasterizationException(string message, Exception inner) : base(message, inner) { }

    public RasterizationException(int codepoint, string details)
        : base($"Error rasterizing glyph for codepoint U+{codepoint:X4}: {details}")
    {
        Codepoint = codepoint;
    }

    public int? Codepoint { get; }
}
