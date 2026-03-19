using Bmfontier.Font.Models;

namespace Bmfontier.Font;

public interface IFontReader
{
    FontInfo ReadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0);
}
