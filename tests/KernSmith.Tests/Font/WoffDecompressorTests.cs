using KernSmith.Font;
using FluentAssertions;

namespace KernSmith.Tests.Font;

public class WoffDecompressorTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Fact]
    public void IsWoff_WithTtfData_ReturnsFalse()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act & Assert
        WoffDecompressor.IsWoff(fontData).Should().BeFalse();
    }

    [Fact]
    public void IsWoff2_WithTtfData_ReturnsFalse()
    {
        // Arrange
        var fontData = LoadTestFont();

        // Act & Assert
        WoffDecompressor.IsWoff2(fontData).Should().BeFalse();
    }

    [Fact]
    public void IsWoff_WithWoffSignature_ReturnsTrue()
    {
        // Arrange
        var data = new byte[] { (byte)'w', (byte)'O', (byte)'F', (byte)'F', 0, 0, 0, 0 };

        // Act & Assert
        WoffDecompressor.IsWoff(data).Should().BeTrue();
    }

    [Fact]
    public void IsWoff2_WithWoff2Signature_ReturnsTrue()
    {
        // Arrange
        var data = new byte[] { (byte)'w', (byte)'O', (byte)'F', (byte)'2', 0, 0, 0, 0 };

        // Act & Assert
        WoffDecompressor.IsWoff2(data).Should().BeTrue();
    }
}
