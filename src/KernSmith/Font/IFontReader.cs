using KernSmith.Font.Models;

namespace KernSmith.Font;

/// <summary>
/// Reads a font file and extracts family metadata, glyph metrics, and kerning pairs.
/// </summary>
public interface IFontReader
{
    /// <summary>
    /// Reads a font file and returns its metadata, metrics, and kerning pairs.
    /// </summary>
    /// <param name="fontData">The font file bytes (TTF, OTF, or WOFF).</param>
    /// <param name="faceIndex">Which face to use in a .ttc font collection. Usually 0.</param>
    FontInfo ReadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0);
}
