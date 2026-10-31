using KernSmith.Rasterizers.Native.Internal.Color;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>The sRGB transfer function COLR version 1 composites through.</summary>
public class SrgbTransferTests
{
    [Fact]
    public void Encode_EqualsTheFormula_AcrossZeroToOneAndAtEveryCodeBoundary()
    {
        // The threshold lookup must give the formula's code for every float; this samples [0, 1]
        // and pins both sides of every point where the formula's code changes.
        int one = BitConverter.SingleToInt32Bits(1f);
        int previous = SrgbTransfer.EncodeDirect(0f);
        int boundaries = 0;
        for (int bits = 0; bits <= one; bits += 4093)
        {
            float x = BitConverter.Int32BitsToSingle(bits);
            int code = SrgbTransfer.EncodeDirect(x);
            SrgbTransfer.Encode(x).ShouldBe((byte)code, $"x = {x:R}");
            if (code != previous)
            {
                // Narrow to the exact bit pattern where the code changes, then check its neighbours.
                int low = bits - 4093, high = bits;
                while (high - low > 1)
                {
                    int middle = low + ((high - low) / 2);
                    if (SrgbTransfer.EncodeDirect(BitConverter.Int32BitsToSingle(middle)) == code)
                        high = middle;
                    else
                        low = middle;
                }

                for (int b = low - 1; b <= high + 1; b++)
                {
                    float y = BitConverter.Int32BitsToSingle(b);
                    SrgbTransfer.Encode(y).ShouldBe(SrgbTransfer.EncodeDirect(y), $"x = {y:R}");
                }

                boundaries++;
                previous = code;
            }
        }

        boundaries.ShouldBeGreaterThan(200, "the sample should cross nearly every code");
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1f)]
    [InlineData(-0f)]
    [InlineData(float.Epsilon)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(float.PositiveInfinity)]
    public void Encode_EqualsTheFormula_OutsideAndAtTheEndsOfTheRange(float x)
    {
        SrgbTransfer.Encode(x).ShouldBe(SrgbTransfer.EncodeDirect(x));
    }
}
