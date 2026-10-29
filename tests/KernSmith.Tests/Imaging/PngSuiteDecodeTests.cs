using KernSmith.Imaging;
using Shouldly;
using StbImageSharp;

namespace KernSmith.Tests.Imaging;

/// <summary>
/// Decodes the official PngSuite (Willem van Schaik; see <c>Fixtures/PngSuite/PngSuite.LICENSE</c>)
/// and compares every pixel with StbImageSharp, used here only as a test oracle.
/// </summary>
public class PngSuiteDecodeTests
{
    /// <summary>The 2017-07-19 suite: 161 valid images and 14 deliberately corrupt ones ('x' prefix).</summary>
    private const int ValidCount = 161;
    private const int CorruptCount = 14;

    public static TheoryData<string> ValidFiles() =>
        new(PngTestFiles.SuiteFileNames().Where(n => !n.StartsWith('x')));

    public static TheoryData<string> CorruptFiles() =>
        new(PngTestFiles.SuiteFileNames().Where(n => n.StartsWith('x')));

    [Fact]
    public void Suite_IsComplete()
    {
        // The theories below enumerate the folder; this keeps them from passing on an empty one.
        var names = PngTestFiles.SuiteFileNames();
        names.Count(n => !n.StartsWith('x')).ShouldBe(ValidCount);
        names.Count(n => n.StartsWith('x')).ShouldBe(CorruptCount);
    }

    [Theory]
    [MemberData(nameof(ValidFiles))]
    public void Decode_MatchesOracle(string fileName)
    {
        // Arrange
        var bytes = PngTestFiles.Read(fileName);
        var expected = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        var header = PngDecoder.ReadHeader(bytes);

        // Act
        var image = PngDecoder.Decode(bytes);

        // Assert -- the oracle drops the low byte of 16-bit samples; this decoder rounds
        // (round(v / 257), the spec's most accurate rescaling), so 16-bit files may differ by one.
        image.Width.ShouldBe(expected.Width);
        image.Height.ShouldBe(expected.Height);
        image.Pixels.Length.ShouldBe(expected.Data.Length);
        int tolerance = header.BitDepth == 16 ? 1 : 0;
        int worst = 0, worstAt = -1;
        for (int i = 0; i < expected.Data.Length; i++)
        {
            int diff = Math.Abs(image.Pixels[i] - expected.Data[i]);
            if (diff > worst)
                (worst, worstAt) = (diff, i);
        }

        worst.ShouldBeLessThanOrEqualTo(tolerance,
            $"{fileName}: byte {worstAt} (pixel {worstAt / 4}, channel {worstAt % 4}) is {(worstAt >= 0 ? image.Pixels[worstAt] : 0)}, oracle {(worstAt >= 0 ? expected.Data[worstAt] : 0)}");
    }

    [Theory]
    [MemberData(nameof(ValidFiles))]
    public void ReadHeader_MatchesFileName(string fileName)
    {
        // PngSuite names encode the header: ...[n|i][color type][bit depth].png, e.g. basi3p04.
        var header = PngDecoder.ReadHeader(PngTestFiles.Read(fileName));

        header.Interlaced.ShouldBe(fileName[3] == 'i');
        ((int)header.ColorType).ShouldBe(fileName[4] - '0');
        header.BitDepth.ShouldBe(int.Parse(fileName.AsSpan(6, 2)));
    }

    [Theory]
    [MemberData(nameof(CorruptFiles))]
    public void Decode_CorruptFile_ThrowsPngFormatException(string fileName)
    {
        var bytes = PngTestFiles.Read(fileName);

        Should.Throw<PngFormatException>(() => PngDecoder.Decode(bytes));
    }

    [Fact]
    public void Decode_InterlacedAndPlainTwins_AreIdentical()
    {
        // basn* and basi* hold the same pictures; Adam7 must reassemble them exactly.
        foreach (var plain in PngTestFiles.SuiteFileNames().Where(n => n.StartsWith("basn", StringComparison.Ordinal)))
        {
            var interlaced = "basi" + plain[4..];
            PngDecoder.Decode(PngTestFiles.Read(interlaced)).Pixels
                .ShouldBe(PngDecoder.Decode(PngTestFiles.Read(plain)).Pixels, interlaced);
        }
    }
}
