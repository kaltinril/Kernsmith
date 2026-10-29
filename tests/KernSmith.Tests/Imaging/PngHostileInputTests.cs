using KernSmith.Imaging;
using Shouldly;
using static KernSmith.Tests.Imaging.PngTestFiles;

namespace KernSmith.Tests.Imaging;

/// <summary>
/// Untrusted input (PNGs embedded in fonts) must either decode or throw
/// <see cref="PngFormatException"/> — never another exception type, never a hang, and never an
/// allocation the header did not justify.
/// </summary>
public class PngHostileInputTests
{
    /// <summary>A small set covering every color type, low and high bit depths, interlacing and palettes.</summary>
    private static readonly string[] Seeds =
    [
        "basn0g01.png", "basn0g16.png", "basn2c08.png", "basn3p04.png", "basn4a16.png", "basn6a08.png",
        "basi0g02.png", "basi2c16.png", "basi3p08.png", "basi6a16.png", "tbbn3p08.png", "s09i3p02.png", "f99n0g04.png",
    ];

    [Fact]
    public void Decode_EveryTruncation_ThrowsPngFormatException()
    {
        foreach (var name in new[] { "basn6a08.png", "basi3p04.png", "basn0g16.png" })
        {
            var bytes = Read(name);
            for (int length = 0; length < bytes.Length; length++)
            {
                var truncated = bytes.AsSpan(0, length).ToArray();
                Should.Throw<PngFormatException>(() => PngDecoder.Decode(truncated), $"{name} cut to {length} bytes");
            }
        }
    }

    [Fact]
    public void Decode_SeededMutations_DecodeOrThrowPngFormatException()
    {
        // Half the mutants keep their CRCs consistent, so the mutation reaches the header checks,
        // the inflater, the filters and the pixel expansion instead of stopping at the CRC.
        var random = new Random(0x504E47);
        int decoded = 0, rejected = 0;
        for (int iteration = 0; iteration < 4000; iteration++)
        {
            var mutant = Read(Seeds[iteration % Seeds.Length]);
            int edits = 1 + random.Next(4);
            for (int e = 0; e < edits; e++)
                Mutate(mutant, random);
            if ((iteration & 1) == 0)
                FixCrcs(mutant);

            try
            {
                var image = PngDecoder.Decode(mutant, new PngDecodeLimits(4096, 4096, 1 << 22));
                image.Pixels.Length.ShouldBe(image.Width * image.Height * 4);
                decoded++;
            }
            catch (PngFormatException)
            {
                rejected++;
            }
        }

        // Both outcomes must actually occur, or the fuzzing is not exercising anything.
        decoded.ShouldBeGreaterThan(100);
        rejected.ShouldBeGreaterThan(100);
    }

    [Fact]
    public void Decode_DecompressionBomb_InflatesOnlyWhatTheHeaderDeclares()
    {
        // A 1x1 gray image whose zlib stream expands to 64 MiB of zeros.
        var bomb = Png(Ihdr(1, 1, 8, 0), Idat(new byte[64 * 1024 * 1024]), Iend());
        long before = GC.GetAllocatedBytesForCurrentThread();

        var image = PngDecoder.Decode(bomb);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        image.Pixels.ShouldBe(new byte[] { 0, 0, 0, 255 });
        allocated.ShouldBeLessThan(4L * 1024 * 1024, "the decoder must not inflate past the declared image");
    }

    [Fact]
    public void Decode_ChunkLengthPastEndOfData_Throws()
    {
        var png = Read("basn0g08.png");
        // IHDR's length field: claim 2^31 - 1 bytes.
        png[8] = 0x7F;
        png[9] = png[10] = png[11] = 0xFF;

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Decode_CorruptDeflateData_Throws()
    {
        var png = Png(Ihdr(4, 4, 8, 0), Chunk("IDAT", [0x78, 0x9C, 0xFF, 0xFF, 0xFF, 0xFF, 0x00]), Iend());

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(png));
    }

    [Fact]
    public void Decode_EmptyInput_Throws()
    {
        Should.Throw<PngFormatException>(() => PngDecoder.Decode([]));
        Should.Throw<PngFormatException>(() => PngDecoder.ReadHeader([]));
    }

    private static void Mutate(byte[] data, Random random)
    {
        int position = random.Next(data.Length);
        switch (random.Next(4))
        {
            case 0:
                data[position] ^= (byte)(1 << random.Next(8));
                break;
            case 1:
                data[position] = (byte)random.Next(256);
                break;
            case 2:
                data[position] = random.Next(2) == 0 ? (byte)0 : (byte)0xFF;
                break;
            default:
                // Swap two bytes, which keeps lengths plausible while scrambling content.
                int other = random.Next(data.Length);
                (data[position], data[other]) = (data[other], data[position]);
                break;
        }
    }
}
