using KernSmith.Font;
using KernSmith.Font.Woff2;
using Shouldly;

namespace KernSmith.Tests.Font;

public class Woff2HmtxTransformTests
{
    [Fact]
    public void LongLocaOffsetNearInt32Max_ThrowsFontParsingException()
    {
        // The hmtx transform restores a side bearing from glyf xMin through loca. A long-loca
        // start of 0x7FFFFFFE overflowed the "start + 4 past glyf" check negative, which then
        // passed, and the read threw ArgumentOutOfRangeException.
        byte[] transformed = [0x01, 0x02, 0x00]; // lsb array omitted; one advance of 512
        byte[] loca = [0x7F, 0xFF, 0xFF, 0xFE, 0x7F, 0xFF, 0xFF, 0xFF];

        Should.Throw<FontParsingException>(() =>
            Woff2HmtxTransform.Reconstruct(transformed, numGlyphs: 1, numberOfHMetrics: 1, glyf: new byte[16], loca, indexFormat: 1));
    }
}
