using KernSmith.Font.Models;

namespace KernSmith.Font;

public interface IFontReader
{
    FontInfo ReadFont(ReadOnlySpan<byte> fontData, int faceIndex = 0);
}
