namespace Bmfontier.Atlas;

internal class SkylinePacker : IAtlasPacker
{
    public PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int maxWidth, int maxHeight)
    {
        throw new NotImplementedException("SkylinePacker is not yet implemented. Use MaxRectsPacker instead.");
    }
}
