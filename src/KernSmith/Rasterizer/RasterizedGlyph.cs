using KernSmith.Font.Models;

namespace KernSmith.Rasterizer;

public sealed class RasterizedGlyph
{
    public required int Codepoint { get; init; }
    public required int GlyphIndex { get; init; }
    public required byte[] BitmapData { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Pitch { get; init; }
    public required GlyphMetrics Metrics { get; init; }
    public required PixelFormat Format { get; init; }
}
