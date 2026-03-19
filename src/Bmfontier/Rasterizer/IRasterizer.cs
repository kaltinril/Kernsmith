namespace Bmfontier.Rasterizer;

public interface IRasterizer : IDisposable
{
    void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0);
    RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options);
    IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options);
}
