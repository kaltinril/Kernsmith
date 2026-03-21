using KernSmith.Atlas;
using FluentAssertions;

namespace KernSmith.Tests;

public sealed class OutputFormatTests
{
    private static byte[] LoadTestFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    // ---------------------------------------------------------------
    // B2.1 — DDS encoder
    // ---------------------------------------------------------------

    [Fact]
    public void DdsEncoder_Rgba_HasCorrectMagicAndHeaderSize()
    {
        // Arrange
        var encoder = new DdsEncoder();
        var width = 4;
        var height = 4;
        var pixelData = new byte[width * height * 4]; // RGBA
        // Fill with a known pattern: R=255, G=128, B=64, A=255
        for (var i = 0; i < pixelData.Length; i += 4)
        {
            pixelData[i + 0] = 255; // R
            pixelData[i + 1] = 128; // G
            pixelData[i + 2] = 64;  // B
            pixelData[i + 3] = 255; // A
        }

        // Act
        var dds = encoder.Encode(pixelData, width, height, PixelFormat.Rgba32);

        // Assert — DDS magic number: "DDS " = 0x20534444 little-endian
        dds[0].Should().Be(0x44, "first byte of DDS magic should be 'D'");
        dds[1].Should().Be(0x44, "second byte of DDS magic should be 'D'");
        dds[2].Should().Be(0x53, "third byte of DDS magic should be 'S'");
        dds[3].Should().Be(0x20, "fourth byte of DDS magic should be ' '");

        // Header size at offset 4: should be 124 (0x7C)
        var headerSize = BitConverter.ToUInt32(dds, 4);
        headerSize.Should().Be(124, "DDS header dwSize should be 124");
    }

    [Fact]
    public void DdsEncoder_Rgba_HasCorrectDimensions()
    {
        // Arrange
        var encoder = new DdsEncoder();
        var width = 8;
        var height = 6;
        var pixelData = new byte[width * height * 4];

        // Act
        var dds = encoder.Encode(pixelData, width, height, PixelFormat.Rgba32);

        // Assert — height at offset 12, width at offset 16 (after magic + dwSize + dwFlags)
        var ddsHeight = BitConverter.ToUInt32(dds, 12);
        var ddsWidth = BitConverter.ToUInt32(dds, 16);
        ddsHeight.Should().Be(6, "DDS header should store the correct height");
        ddsWidth.Should().Be(8, "DDS header should store the correct width");
    }

    [Fact]
    public void DdsEncoder_Rgba_ConvertsPixelsToBgraOrder()
    {
        // Arrange
        var encoder = new DdsEncoder();
        var width = 1;
        var height = 1;
        var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }; // R=AA, G=BB, B=CC, A=DD

        // Act
        var dds = encoder.Encode(pixelData, width, height, PixelFormat.Rgba32);

        // Assert — pixel data starts at offset 128 (4 magic + 124 header)
        // DDS stores as BGRA
        var pixelOffset = 128;
        dds[pixelOffset + 0].Should().Be(0xCC, "DDS pixel B channel should be source B (0xCC)");
        dds[pixelOffset + 1].Should().Be(0xBB, "DDS pixel G channel should be source G (0xBB)");
        dds[pixelOffset + 2].Should().Be(0xAA, "DDS pixel R channel should be source R (0xAA)");
        dds[pixelOffset + 3].Should().Be(0xDD, "DDS pixel A channel should be source A (0xDD)");
    }

    [Fact]
    public void DdsEncoder_Rgba_HasCorrectTotalSize()
    {
        // Arrange
        var encoder = new DdsEncoder();
        var width = 4;
        var height = 4;
        var pixelData = new byte[width * height * 4];

        // Act
        var dds = encoder.Encode(pixelData, width, height, PixelFormat.Rgba32);

        // Assert — total size = 4 (magic) + 124 (header) + width * height * 4 (pixels)
        var expectedSize = 4 + 124 + width * height * 4;
        dds.Length.Should().Be(expectedSize, "DDS file size should be magic + header + pixel data");
    }

    [Fact]
    public void DdsEncoder_Rgba_PixelFormatFlags_IndicateAlphaAndRgb()
    {
        // Arrange
        var encoder = new DdsEncoder();
        var pixelData = new byte[4]; // 1x1 RGBA

        // Act
        var dds = encoder.Encode(pixelData, 1, 1, PixelFormat.Rgba32);

        // Assert — pixel format flags at offset 80 (4 magic + 76 into header)
        // Should contain DDPF_ALPHAPIXELS (0x1) | DDPF_RGB (0x40) = 0x41
        var pfFlags = BitConverter.ToUInt32(dds, 80);
        pfFlags.Should().Be(0x41, "RGBA format should set DDPF_ALPHAPIXELS | DDPF_RGB flags");

        // RGB bit count at offset 88 should be 32
        var bitCount = BitConverter.ToUInt32(dds, 88);
        bitCount.Should().Be(32, "RGBA format should report 32 bits per pixel");
    }

    [Fact]
    public void DdsEncoder_Grayscale_HasLuminanceFlags()
    {
        // Arrange
        var encoder = new DdsEncoder();
        var width = 2;
        var height = 2;
        var pixelData = new byte[width * height]; // Grayscale

        // Act
        var dds = encoder.Encode(pixelData, width, height, PixelFormat.Grayscale8);

        // Assert — pixel format flags should contain DDPF_LUMINANCE (0x20000)
        var pfFlags = BitConverter.ToUInt32(dds, 80);
        pfFlags.Should().Be(0x20000, "Grayscale format should set DDPF_LUMINANCE flag");

        // Bit count should be 8
        var bitCount = BitConverter.ToUInt32(dds, 88);
        bitCount.Should().Be(8, "Grayscale format should report 8 bits per pixel");
    }

    [Fact]
    public void DdsEncoder_Grayscale_CopiesPixelDataDirectly()
    {
        // Arrange
        var encoder = new DdsEncoder();
        var width = 2;
        var height = 2;
        var pixelData = new byte[] { 10, 20, 30, 40 };

        // Act
        var dds = encoder.Encode(pixelData, width, height, PixelFormat.Grayscale8);

        // Assert — pixel data starts at offset 128
        dds[128].Should().Be(10);
        dds[129].Should().Be(20);
        dds[130].Should().Be(30);
        dds[131].Should().Be(40);
    }

    // ---------------------------------------------------------------
    // B2.2 — TGA encoder
    // ---------------------------------------------------------------

    [Fact]
    public void TgaEncoder_Rgba_HasCorrectHeader()
    {
        // Arrange
        var encoder = new TgaEncoder();
        var width = 4;
        var height = 4;
        var pixelData = new byte[width * height * 4];

        // Act
        var tga = encoder.Encode(pixelData, width, height, PixelFormat.Rgba32);

        // Assert — TGA header is 18 bytes
        tga[0].Should().Be(0, "ID length should be 0");
        tga[1].Should().Be(0, "color map type should be 0 (none)");
        tga[2].Should().Be(2, "image type should be 2 (uncompressed true-color)");

        // Width at bytes 12-13 (little-endian)
        var tgaWidth = tga[12] | (tga[13] << 8);
        tgaWidth.Should().Be(4, "TGA header should store the correct width");

        // Height at bytes 14-15 (little-endian)
        var tgaHeight = tga[14] | (tga[15] << 8);
        tgaHeight.Should().Be(4, "TGA header should store the correct height");

        // Bits per pixel
        tga[16].Should().Be(32, "RGBA TGA should report 32 bits per pixel");

        // Image descriptor: top-left origin + 8 alpha bits = 0x28
        tga[17].Should().Be(0x28, "image descriptor should indicate top-left origin with 8 alpha bits");
    }

    [Fact]
    public void TgaEncoder_Rgba_ConvertsPixelsToBgraOrder()
    {
        // Arrange
        var encoder = new TgaEncoder();
        var width = 1;
        var height = 1;
        var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }; // R=AA, G=BB, B=CC, A=DD

        // Act
        var tga = encoder.Encode(pixelData, width, height, PixelFormat.Rgba32);

        // Assert — pixel data starts at offset 18, stored as BGRA
        tga[18].Should().Be(0xCC, "TGA pixel B channel should be source B (0xCC)");
        tga[19].Should().Be(0xBB, "TGA pixel G channel should be source G (0xBB)");
        tga[20].Should().Be(0xAA, "TGA pixel R channel should be source R (0xAA)");
        tga[21].Should().Be(0xDD, "TGA pixel A channel should be source A (0xDD)");
    }

    [Fact]
    public void TgaEncoder_Rgba_HasCorrectTotalSize()
    {
        // Arrange
        var encoder = new TgaEncoder();
        var width = 4;
        var height = 4;
        var pixelData = new byte[width * height * 4];

        // Act
        var tga = encoder.Encode(pixelData, width, height, PixelFormat.Rgba32);

        // Assert — total size = 18 (header) + pixel data length
        tga.Length.Should().Be(18 + pixelData.Length, "TGA file size should be header + pixel data");
    }

    [Fact]
    public void TgaEncoder_Grayscale_HasCorrectHeader()
    {
        // Arrange
        var encoder = new TgaEncoder();
        var width = 4;
        var height = 4;
        var pixelData = new byte[width * height];

        // Act
        var tga = encoder.Encode(pixelData, width, height, PixelFormat.Grayscale8);

        // Assert
        tga[2].Should().Be(3, "image type should be 3 (uncompressed grayscale)");
        tga[16].Should().Be(8, "grayscale TGA should report 8 bits per pixel");
        tga[17].Should().Be(0x20, "image descriptor should indicate top-left origin");
    }

    [Fact]
    public void TgaEncoder_Grayscale_CopiesPixelDataDirectly()
    {
        // Arrange
        var encoder = new TgaEncoder();
        var width = 2;
        var height = 2;
        var pixelData = new byte[] { 100, 150, 200, 250 };

        // Act
        var tga = encoder.Encode(pixelData, width, height, PixelFormat.Grayscale8);

        // Assert — grayscale data starts at offset 18, no byte order conversion
        tga[18].Should().Be(100);
        tga[19].Should().Be(150);
        tga[20].Should().Be(200);
        tga[21].Should().Be(250);
    }

    [Fact]
    public void TgaEncoder_LargerImage_EncodesWidthAndHeightCorrectly()
    {
        // Arrange
        var encoder = new TgaEncoder();
        var width = 512;
        var height = 300;
        var pixelData = new byte[width * height]; // Grayscale

        // Act
        var tga = encoder.Encode(pixelData, width, height, PixelFormat.Grayscale8);

        // Assert — verify 16-bit little-endian width/height
        var tgaWidth = tga[12] | (tga[13] << 8);
        tgaWidth.Should().Be(512, "TGA should encode width 512 correctly in two bytes");

        var tgaHeight = tga[14] | (tga[15] << 8);
        tgaHeight.Should().Be(300, "TGA should encode height 300 correctly in two bytes");
    }

    // ---------------------------------------------------------------
    // B2.3 — ToFile() integration
    // ---------------------------------------------------------------

    [Fact]
    public void ToFile_Text_WritesFntAndPngFiles()
    {
        // Arrange
        var fontData = LoadTestFont();
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii
        });

        var tempDir = Path.Combine(Path.GetTempPath(), $"KernSmith_ToFile_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var outputPath = Path.Combine(tempDir, "testfont");

            // Act
            result.ToFile(outputPath);

            // Assert — .fnt file should exist and be non-empty
            var fntPath = outputPath + ".fnt";
            File.Exists(fntPath).Should().BeTrue("ToFile should create a .fnt file");
            new FileInfo(fntPath).Length.Should().BeGreaterThan(0, ".fnt file should be non-empty");

            // Verify .fnt content starts with the BMFont text format
            var fntContent = File.ReadAllText(fntPath);
            fntContent.Should().StartWith("info ", ".fnt file should contain BMFont text format");

            // Assert — .png file(s) should exist and be non-empty
            var pngFiles = Directory.GetFiles(tempDir, "*.png");
            pngFiles.Should().HaveCountGreaterThan(0, "ToFile should create at least one .png file");

            foreach (var pngFile in pngFiles)
            {
                new FileInfo(pngFile).Length.Should().BeGreaterThan(0, $"PNG file {Path.GetFileName(pngFile)} should be non-empty");
            }
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ToFile_Xml_WritesXmlFntFile()
    {
        // Arrange
        var fontData = LoadTestFont();
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("ABC")
        });

        var tempDir = Path.Combine(Path.GetTempPath(), $"KernSmith_ToFileXml_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var outputPath = Path.Combine(tempDir, "testfont");

            // Act
            result.ToFile(outputPath, OutputFormat.Xml);

            // Assert — .fnt file should contain XML content
            var fntPath = outputPath + ".fnt";
            File.Exists(fntPath).Should().BeTrue("ToFile with XML format should create a .fnt file");

            var fntContent = File.ReadAllText(fntPath);
            fntContent.Should().Contain("<font>", "XML format .fnt file should contain the font root element");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ToFile_Binary_WritesBinaryFntFile()
    {
        // Arrange
        var fontData = LoadTestFont();
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 24,
            Characters = CharacterSet.FromChars("ABC")
        });

        var tempDir = Path.Combine(Path.GetTempPath(), $"KernSmith_ToFileBin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var outputPath = Path.Combine(tempDir, "testfont");

            // Act
            result.ToFile(outputPath, OutputFormat.Binary);

            // Assert — .fnt file should contain BMF binary header
            var fntPath = outputPath + ".fnt";
            File.Exists(fntPath).Should().BeTrue("ToFile with Binary format should create a .fnt file");

            var fntBytes = File.ReadAllBytes(fntPath);
            fntBytes[0].Should().Be(66, "binary .fnt should start with 'B'");
            fntBytes[1].Should().Be(77, "binary .fnt should start with 'M'");
            fntBytes[2].Should().Be(70, "binary .fnt should start with 'F'");
            fntBytes[3].Should().Be(3, "binary .fnt should be version 3");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ToFile_PngFilesMatchPageCount()
    {
        // Arrange
        var fontData = LoadTestFont();
        var result = BmFont.Generate(fontData, new FontGeneratorOptions
        {
            Size = 32,
            Characters = CharacterSet.Ascii
        });

        var tempDir = Path.Combine(Path.GetTempPath(), $"KernSmith_ToFilePages_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var outputPath = Path.Combine(tempDir, "testfont");

            // Act
            result.ToFile(outputPath);

            // Assert — number of .png files should match atlas page count
            var pngFiles = Directory.GetFiles(tempDir, "*.png");
            pngFiles.Should().HaveCount(result.Pages.Count,
                "number of PNG files written should match the number of atlas pages");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
