using KernSmith.Atlas;
using Shouldly;

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
        dds[0].ShouldBe((byte)0x44);
        dds[1].ShouldBe((byte)0x44);
        dds[2].ShouldBe((byte)0x53);
        dds[3].ShouldBe((byte)0x20);

        // Header size at offset 4: should be 124 (0x7C)
        var headerSize = BitConverter.ToUInt32(dds, 4);
        headerSize.ShouldBe((uint)124);
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
        ddsHeight.ShouldBe((uint)6);
        ddsWidth.ShouldBe((uint)8);
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
        dds[pixelOffset + 0].ShouldBe((byte)0xCC);
        dds[pixelOffset + 1].ShouldBe((byte)0xBB);
        dds[pixelOffset + 2].ShouldBe((byte)0xAA);
        dds[pixelOffset + 3].ShouldBe((byte)0xDD);
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
        dds.Length.ShouldBe(expectedSize);
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
        pfFlags.ShouldBe((uint)0x41);

        // RGB bit count at offset 88 should be 32
        var bitCount = BitConverter.ToUInt32(dds, 88);
        bitCount.ShouldBe((uint)32);
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
        pfFlags.ShouldBe((uint)0x20000);

        // Bit count should be 8
        var bitCount = BitConverter.ToUInt32(dds, 88);
        bitCount.ShouldBe((uint)8);
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
        dds[128].ShouldBe((byte)10);
        dds[129].ShouldBe((byte)20);
        dds[130].ShouldBe((byte)30);
        dds[131].ShouldBe((byte)40);
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
        tga[0].ShouldBe((byte)0);
        tga[1].ShouldBe((byte)0);
        tga[2].ShouldBe((byte)2);

        // Width at bytes 12-13 (little-endian)
        var tgaWidth = tga[12] | (tga[13] << 8);
        tgaWidth.ShouldBe(4);

        // Height at bytes 14-15 (little-endian)
        var tgaHeight = tga[14] | (tga[15] << 8);
        tgaHeight.ShouldBe(4);

        // Bits per pixel
        tga[16].ShouldBe((byte)32);

        // Image descriptor: top-left origin + 8 alpha bits = 0x28
        tga[17].ShouldBe((byte)0x28);
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
        tga[18].ShouldBe((byte)0xCC);
        tga[19].ShouldBe((byte)0xBB);
        tga[20].ShouldBe((byte)0xAA);
        tga[21].ShouldBe((byte)0xDD);
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
        tga.Length.ShouldBe(18 + pixelData.Length);
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
        tga[2].ShouldBe((byte)3);
        tga[16].ShouldBe((byte)8);
        tga[17].ShouldBe((byte)0x20);
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
        tga[18].ShouldBe((byte)100);
        tga[19].ShouldBe((byte)150);
        tga[20].ShouldBe((byte)200);
        tga[21].ShouldBe((byte)250);
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
        tgaWidth.ShouldBe(512);

        var tgaHeight = tga[14] | (tga[15] << 8);
        tgaHeight.ShouldBe(300);
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
            File.Exists(fntPath).ShouldBeTrue("ToFile should create a .fnt file");
            new FileInfo(fntPath).Length.ShouldBeGreaterThan(0);

            // Verify .fnt content starts with the BMFont text format
            var fntContent = File.ReadAllText(fntPath);
            fntContent.ShouldStartWith("info ");

            // Assert — .png file(s) should exist and be non-empty
            var pngFiles = Directory.GetFiles(tempDir, "*.png");
            pngFiles.Length.ShouldBeGreaterThan(0);

            foreach (var pngFile in pngFiles)
            {
                new FileInfo(pngFile).Length.ShouldBeGreaterThan(0);
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
            File.Exists(fntPath).ShouldBeTrue("ToFile with XML format should create a .fnt file");

            var fntContent = File.ReadAllText(fntPath);
            fntContent.ShouldContain("<font>");
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
            File.Exists(fntPath).ShouldBeTrue("ToFile with Binary format should create a .fnt file");

            var fntBytes = File.ReadAllBytes(fntPath);
            fntBytes[0].ShouldBe((byte)66);
            fntBytes[1].ShouldBe((byte)77);
            fntBytes[2].ShouldBe((byte)70);
            fntBytes[3].ShouldBe((byte)3);
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
            pngFiles.Length.ShouldBe(result.Pages.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
