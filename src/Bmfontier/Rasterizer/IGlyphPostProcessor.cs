namespace Bmfontier.Rasterizer;

public interface IGlyphPostProcessor
{
    RasterizedGlyph Process(RasterizedGlyph glyph);
}
