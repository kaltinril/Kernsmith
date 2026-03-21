namespace KernSmith.Atlas;

public interface IAtlasPacker
{
    PackResult Pack(IReadOnlyList<GlyphRect> glyphs, int maxWidth, int maxHeight);
}
