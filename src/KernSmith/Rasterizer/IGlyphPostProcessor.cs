namespace KernSmith.Rasterizer;

public interface IGlyphPostProcessor
{
    RasterizedGlyph Process(RasterizedGlyph glyph);
}
