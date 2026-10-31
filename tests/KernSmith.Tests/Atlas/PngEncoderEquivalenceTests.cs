using KernSmith.Atlas;
using KernSmith.Imaging;
using Shouldly;
using StbImageSharp;

namespace KernSmith.Tests.Atlas;

/// <summary>
/// <see cref="PngEncoder"/> must encode exactly the pixels it is given: every image decodes, through
/// two independent decoders (StbImageSharp, and KernSmith.Imaging's strict decoder, which checks
/// chunk framing and CRCs), to the input bytes and to what the StbImageWriteSharp encoder it replaced
/// (<see cref="ReferenceStbPngEncoder"/>) decodes to. The PNG bytes themselves differ: the deflate
/// stream is the runtime's zlib, not stb's.
/// </summary>
[Collection("RasterizerFactory")]
public class PngEncoderEquivalenceTests
{
    private static readonly int[] Sizes = [1, 2, 3, 7, 16, 63, 255, 256, 300];

    public static TheoryData<PixelFormat, int> RandomCases()
    {
        var data = new TheoryData<PixelFormat, int>();
        foreach (var format in new[] { PixelFormat.Grayscale8, PixelFormat.Rgba32 })
            for (int seed = 0; seed < 12; seed++)
                data.Add(format, seed);
        return data;
    }

    [Theory]
    [MemberData(nameof(RandomCases))]
    public void RandomImage_DecodesToInput_AndToReferencePixels(PixelFormat format, int seed)
    {
        var rng = new Random(seed);
        int width = Sizes[rng.Next(Sizes.Length)];
        int height = Sizes[rng.Next(Sizes.Length)];
        var pixels = RandomPixels(rng, width * height * Bpp(format), seed % 3);

        AssertEquivalent(pixels, width, height, format);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedAtlasPage_DecodesToInput_AndToReferencePixels(bool rgba)
    {
        var result = BmFont.Generate(LoadFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.ExtendedAscii,
            Outline = rgba ? 2 : 0,
            OutlineR = 255,
        });

        var page = result.Pages[0];
        page.Format.ShouldBe(rgba ? PixelFormat.Rgba32 : PixelFormat.Grayscale8);
        AssertEquivalent(page.PixelData, page.Width, page.Height, page.Format);
    }

    [Fact]
    public void GeneratedAtlasPage_IsSmallerThanReference()
    {
        var page = BmFont.Generate(LoadFont(), new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.ExtendedAscii,
        }).Pages[0];

        var encoded = new PngEncoder().Encode(page.PixelData, page.Width, page.Height, page.Format);
        var reference = ReferenceStbPngEncoder.Encode(page.PixelData, page.Width, page.Height, page.Format);

        encoded.Length.ShouldBeLessThan(reference.Length);
    }

    [Fact]
    public void Encode_IsDeterministic()
    {
        var pixels = RandomPixels(new Random(7), 64 * 48 * 4, 1);
        var encoder = new PngEncoder();

        encoder.Encode(pixels, 64, 48, PixelFormat.Rgba32)
            .ShouldBe(encoder.Encode(pixels, 64, 48, PixelFormat.Rgba32));
    }

    [Fact]
    public void Encode_IgnoresBytesPastTheImage()
    {
        // Callers may hand over a larger (e.g. pooled) buffer; only width * height * bpp is the image.
        var pixels = RandomPixels(new Random(3), 10 * 10 + 50, 0);
        var exact = pixels.AsSpan(0, 100).ToArray();
        var encoder = new PngEncoder();

        encoder.Encode(pixels, 10, 10, PixelFormat.Grayscale8)
            .ShouldBe(encoder.Encode(exact, 10, 10, PixelFormat.Grayscale8));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    public void Encode_RejectsEmptyOrNegativeDimensions(int width, int height)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new PngEncoder().Encode(new byte[64], width, height, PixelFormat.Grayscale8));
    }

    [Fact]
    public void Encode_RejectsShortPixelData()
    {
        Should.Throw<ArgumentException>(
            () => new PngEncoder().Encode(new byte[15], 4, 4, PixelFormat.Grayscale8));
        Should.Throw<ArgumentException>(
            () => new PngEncoder().Encode(new byte[63], 4, 4, PixelFormat.Rgba32));
    }

    [Fact]
    public void Encode_RejectsUnknownFormat()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new PngEncoder().Encode(new byte[64], 4, 4, (PixelFormat)99));
    }

    [Fact]
    public void Crc_MatchesBytewiseReference_AtEveryLengthAndAlignment()
    {
        var rng = new Random(11);
        var data = new byte[300];
        rng.NextBytes(data);
        for (int start = 0; start < 9; start++)
            for (int length = 0; start + length <= data.Length; length++)
            {
                var span = data.AsSpan(start, length);
                PngCrc32.Compute(span).ShouldBe(BytewiseCrc(span), $"start {start}, length {length}");
            }

        PngCrc32.Compute("IEND"u8).ShouldBe(0xAE426082u); // the CRC every PNG ends with
    }

    private static uint BytewiseCrc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }

    private static void AssertEquivalent(byte[] pixels, int width, int height, PixelFormat format)
    {
        var encoded = new PngEncoder().Encode(pixels, width, height, format);
        var reference = ReferenceStbPngEncoder.Encode(pixels, width, height, format);
        var image = pixels.AsSpan(0, width * height * Bpp(format)).ToArray();

        var components = format == PixelFormat.Rgba32 ? ColorComponents.RedGreenBlueAlpha : ColorComponents.Grey;
        var decoded = ImageResult.FromMemory(encoded, components);
        (decoded.Width, decoded.Height).ShouldBe((width, height));
        decoded.Data.ShouldBe(image);
        decoded.Data.ShouldBe(ImageResult.FromMemory(reference, components).Data);

        // The strict decoder verifies every chunk's framing and CRC; it always returns RGBA.
        var strict = PngDecoder.Decode(encoded);
        (strict.Width, strict.Height).ShouldBe((width, height));
        strict.Pixels.ShouldBe(PngDecoder.Decode(reference).Pixels);
        strict.Pixels.ShouldBe(format == PixelFormat.Rgba32 ? image : GrayToRgba(image));
    }

    private static byte[] RandomPixels(Random rng, int length, int style)
    {
        var pixels = new byte[length];
        switch (style)
        {
            case 0: // noise: incompressible
                rng.NextBytes(pixels);
                break;
            case 1: // sparse coverage, like an atlas page
                for (int i = 0; i < length; i++)
                    pixels[i] = rng.Next(8) == 0 ? (byte)rng.Next(256) : (byte)0;
                break;
            default: // runs of one value
                for (int i = 0; i < length;)
                {
                    byte v = (byte)rng.Next(256);
                    int run = rng.Next(1, 40);
                    for (int k = 0; k < run && i < length; k++, i++)
                        pixels[i] = v;
                }
                break;
        }

        return pixels;
    }

    private static byte[] GrayToRgba(byte[] gray)
    {
        var rgba = new byte[gray.Length * 4];
        for (int i = 0; i < gray.Length; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = gray[i];
            rgba[i * 4 + 3] = 255;
        }

        return rgba;
    }

    private static int Bpp(PixelFormat format) => format == PixelFormat.Rgba32 ? 4 : 1;

    private static byte[] LoadFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));
}
