using System.Buffers.Binary;
using KernSmith.Imaging;
using Shouldly;
using static KernSmith.Tests.Imaging.PngTestFiles;

namespace KernSmith.Tests.Imaging;

/// <summary>PNG semantics checked on hand-built images with known pixels.</summary>
public class PngDecoderTests
{
    [Fact]
    public void HasSignature_RecognizesOnlyThePngSignature()
    {
        PngDecoder.HasSignature(Signature).ShouldBeTrue();
        PngDecoder.HasSignature(Signature.AsSpan(0, 7)).ShouldBeFalse();
        PngDecoder.HasSignature("GIF89a\0\0"u8).ShouldBeFalse();
    }

    [Fact]
    public void Decode_Rgba8_ReturnsStraightAlphaUnchanged()
    {
        // 2x1 RGBA: a half-transparent red and a fully transparent pixel with color.
        var png = Png(Ihdr(2, 1, 8, 6), Idat([0, 255, 0, 0, 128, 10, 20, 30, 0]), Iend());

        var image = PngDecoder.Decode(png);

        image.Width.ShouldBe(2);
        image.Height.ShouldBe(1);
        image.Pixels.ShouldBe(new byte[] { 255, 0, 0, 128, 10, 20, 30, 0 });
    }

    [Theory]
    [InlineData(1, new byte[] { 0b1010_0000 }, new byte[] { 255, 0, 255, 0 })]
    [InlineData(2, new byte[] { 0b0001_1011 }, new byte[] { 0, 85, 170, 255 })]
    [InlineData(4, new byte[] { 0x0F, 0x58 }, new byte[] { 0, 255, 85, 136 })]
    public void Decode_LowBitGray_ScalesByBitReplication(int bitDepth, byte[] row, byte[] expectedGray)
    {
        var raw = new byte[1 + row.Length];
        row.CopyTo(raw, 1);
        var png = Png(Ihdr(4, 1, bitDepth, 0), Idat(raw), Iend());

        var image = PngDecoder.Decode(png);

        for (int x = 0; x < 4; x++)
        {
            image.Pixels[(x * 4) + 0].ShouldBe(expectedGray[x]);
            image.Pixels[(x * 4) + 1].ShouldBe(expectedGray[x]);
            image.Pixels[(x * 4) + 2].ShouldBe(expectedGray[x]);
            image.Pixels[(x * 4) + 3].ShouldBe((byte)255);
        }
    }

    [Fact]
    public void Decode_Gray16_RoundsToNearest()
    {
        // round(v / 257): 0x00FF -> 0.99 -> 1 (dropping the low byte would give 0); 0x0080 -> 0.498 -> 0.
        var png = Png(Ihdr(2, 1, 16, 0), Idat([0, 0x00, 0xFF, 0x00, 0x80]), Iend());

        var image = PngDecoder.Decode(png);

        image.Pixels[0].ShouldBe((byte)1);
        image.Pixels[4].ShouldBe((byte)0);
    }

    [Fact]
    public void Decode_Gray16Transparency_ComparesAllSixteenBits()
    {
        // tRNS = 0x1234. 0x1200 has the same high byte but is not the transparent value.
        var png = Png(Ihdr(2, 1, 16, 0), Chunk("tRNS", [0x12, 0x34]), Idat([0, 0x12, 0x34, 0x12, 0x00]), Iend());

        var image = PngDecoder.Decode(png);

        image.Pixels[3].ShouldBe((byte)0);
        image.Pixels[7].ShouldBe((byte)255);
    }

    [Fact]
    public void Decode_LowBitGrayTransparency_UsesTheLowBitsOfTheValue()
    {
        // 2-bit gray, tRNS sample 0xFF02: only the low 2 bits (2) count.
        var png = Png(Ihdr(4, 1, 2, 0), Chunk("tRNS", [0xFF, 0x02]), Idat([0, 0b0001_1011]), Iend());

        var image = PngDecoder.Decode(png);

        image.Pixels[3].ShouldBe((byte)255);
        image.Pixels[7].ShouldBe((byte)255);
        image.Pixels[11].ShouldBe((byte)0);
        image.Pixels[15].ShouldBe((byte)255);
    }

    [Fact]
    public void Decode_TruecolorTransparency_MatchesTheExactColor()
    {
        var png = Png(Ihdr(2, 1, 8, 2), Chunk("tRNS", [0, 1, 0, 2, 0, 3]), Idat([0, 1, 2, 3, 1, 2, 4]), Iend());

        var image = PngDecoder.Decode(png);

        image.Pixels.ShouldBe(new byte[] { 1, 2, 3, 0, 1, 2, 4, 255 });
    }

    [Fact]
    public void Decode_Palette_UsesPlteAndShortTrns()
    {
        // tRNS shorter than PLTE: remaining entries are opaque.
        var png = Png(
            Ihdr(3, 1, 8, 3),
            Chunk("PLTE", [10, 20, 30, 40, 50, 60, 70, 80, 90]),
            Chunk("tRNS", [7]),
            Idat([0, 0, 1, 2]),
            Iend());

        var image = PngDecoder.Decode(png);

        image.Pixels.ShouldBe(new byte[] { 10, 20, 30, 7, 40, 50, 60, 255, 70, 80, 90, 255 });
    }

    [Fact]
    public void Decode_PaletteIndexOutOfRange_Throws()
    {
        var png = Png(Ihdr(1, 1, 8, 3), Chunk("PLTE", [1, 2, 3]), Idat([0, 1]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png)).Message.ShouldContain("palette", Case.Insensitive);
    }

    [Fact]
    public void Decode_GrayAlpha16_KeepsAlpha()
    {
        var png = Png(Ihdr(1, 1, 16, 4), Idat([0, 0xFF, 0xFF, 0x80, 0x00]), Iend());

        PngDecoder.Decode(png).Pixels.ShouldBe(new byte[] { 255, 255, 255, 128 });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Decode_EachFilterType_ReconstructsTheRows(int filter)
    {
        // Three rows of a 3-pixel RGB image, all filtered with one type; the encoder side is
        // written out from the spec's definitions.
        byte[][] rows =
        [
            [10, 200, 30, 250, 5, 60, 0, 255, 128],
            [20, 190, 40, 1, 2, 3, 100, 100, 100],
            [255, 0, 255, 0, 255, 0, 17, 34, 51],
        ];
        var raw = new List<byte>();
        byte[] prior = new byte[9];
        foreach (var row in rows)
        {
            raw.Add((byte)filter);
            for (int i = 0; i < row.Length; i++)
            {
                int a = i >= 3 ? row[i - 3] : 0, b = prior[i], c = i >= 3 ? prior[i - 3] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    _ => Paeth(a, b, c),
                };
                raw.Add((byte)(row[i] - predictor));
            }

            prior = row;
        }

        var image = PngDecoder.Decode(Png(Ihdr(3, 3, 8, 2), Idat(raw.ToArray()), Iend()));

        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 3; x++)
                image.Pixels.AsSpan(((y * 3) + x) * 4, 3).ToArray().ShouldBe(rows[y].AsSpan(x * 3, 3).ToArray());
    }

    [Fact]
    public void Decode_UnknownFilterType_Throws()
    {
        var png = Png(Ihdr(1, 1, 8, 0), Idat([5, 0]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Decode_IdatSplitAcrossChunks_IsOneStream()
    {
        var stream = Zlib([0, 1, 2, 3, 0, 4, 5, 6]);
        var png = Png(
            Ihdr(1, 2, 8, 2),
            Chunk("IDAT", stream.AsSpan(0, 1)),
            Chunk("IDAT", []),
            Chunk("IDAT", stream.AsSpan(1, 3)),
            Chunk("IDAT", stream.AsSpan(4)),
            Iend());

        PngDecoder.Decode(png).Pixels.ShouldBe(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 });
    }

    [Fact]
    public void Decode_UnknownAncillaryChunk_IsIgnored()
    {
        var png = Png(Ihdr(1, 1, 8, 0), Chunk("abCd", [1, 2, 3]), Idat([0, 9]), Chunk("zzZz", []), Iend());

        PngDecoder.Decode(png).Pixels.ShouldBe(new byte[] { 9, 9, 9, 255 });
    }

    [Fact]
    public void Decode_ApngChunks_AreIgnoredAndTheDefaultImageDecodes()
    {
        var actl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, 1);
        var png = Png(Ihdr(1, 1, 8, 0), Chunk("acTL", actl), Chunk("fcTL", new byte[26]), Idat([0, 9]), Chunk("fdAT", new byte[8]), Iend());

        PngDecoder.Decode(png).Pixels.ShouldBe(new byte[] { 9, 9, 9, 255 });
    }

    [Fact]
    public void Decode_UnknownCriticalChunk_Throws()
    {
        var png = Png(Ihdr(1, 1, 8, 0), Chunk("ABCD", []), Idat([0, 9]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Decode_IdatChunksNotConsecutive_Throws()
    {
        var stream = Zlib([0, 9]);
        var png = Png(Ihdr(1, 1, 8, 0), Chunk("IDAT", stream.AsSpan(0, 2)), Chunk("teXt", "a\0b"u8), Chunk("IDAT", stream.AsSpan(2)), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Decode_CrcMismatch_Throws()
    {
        var png = Png(Ihdr(1, 1, 8, 0), Chunk("tEXt", "a\0b"u8, corruptCrc: true), Idat([0, 9]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png)).Message.ShouldContain("CRC", Case.Sensitive);
    }

    [Fact]
    public void Decode_MissingIend_Throws()
    {
        var png = Png(Ihdr(1, 1, 8, 0), Idat([0, 9]));

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Decode_PaletteImageWithoutPlte_Throws()
    {
        var png = Png(Ihdr(1, 1, 8, 3), Idat([0, 0]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Decode_TooLittleImageData_Throws()
    {
        // Two rows declared, one supplied.
        var png = Png(Ihdr(1, 2, 8, 0), Idat([0, 9]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void ReadHeader_InvalidDimensions_Throws(int width, int height)
    {
        var png = Png(Ihdr(width, height, 8, 0), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.ReadHeader(png));
    }

    [Theory]
    [InlineData(0, 3)]  // gray: 1, 2, 4, 8, 16
    [InlineData(2, 4)]  // truecolor: 8, 16
    [InlineData(3, 16)] // indexed: 1, 2, 4, 8
    [InlineData(4, 1)]  // gray + alpha: 8, 16
    [InlineData(6, 2)]  // truecolor + alpha: 8, 16
    [InlineData(5, 8)]  // no such color type
    public void ReadHeader_InvalidColorTypeAndDepth_Throws(int colorType, int bitDepth)
    {
        var png = Png(Ihdr(1, 1, bitDepth, colorType), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.ReadHeader(png));
    }

    [Fact]
    public void ReadHeader_ReportsTheHeader()
    {
        var header = PngDecoder.ReadHeader(Png(Ihdr(300, 20, 4, 3, interlace: 1), Iend()));

        header.ShouldBe(new PngHeader(300, 20, 4, PngColorType.Indexed, Interlaced: true));
    }

    [Fact]
    public void Decode_OverTheLimits_ThrowsBeforeInflating()
    {
        var png = Png(Ihdr(64, 64, 8, 6), Idat(new byte[64 * (1 + (64 * 4))]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png, new PngDecodeLimits(63, 4096, 1 << 20)));
        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png, new PngDecodeLimits(4096, 63, 1 << 20)));
        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png, new PngDecodeLimits(4096, 4096, (64 * 64) - 1)));
        PngDecoder.Decode(png, new PngDecodeLimits(64, 64, 64 * 64)).Width.ShouldBe(64);
    }

    [Fact]
    public void Decode_HugeDeclaredSize_ThrowsWithDefaultLimits()
    {
        // 2^31-1 square: within the format, far past the default limits.
        var png = Png(Ihdr(int.MaxValue, int.MaxValue, 16, 6), Idat([0]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
