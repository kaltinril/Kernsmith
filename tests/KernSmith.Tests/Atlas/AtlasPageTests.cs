using KernSmith.Atlas;
using Shouldly;

namespace KernSmith.Tests.Atlas;

public class AtlasPageTests
{
    private static AtlasPage MakeGrayscalePage(int width, int height, byte[] pixels) =>
        new()
        {
            PageIndex = 0,
            Width = width,
            Height = height,
            PixelData = pixels,
            Format = PixelFormat.Grayscale8
        };

    private static AtlasPage MakeRgbaPage(int width, int height, byte[] pixels) =>
        new()
        {
            PageIndex = 0,
            Width = width,
            Height = height,
            PixelData = pixels,
            Format = PixelFormat.Rgba32
        };

    // ---------------------------------------------------------------
    // GetRgbaPixelData
    // ---------------------------------------------------------------

    [Fact]
    public void GetRgbaPixelData_FromGrayscalePage_ExpandsToWhiteWithAlpha()
    {
        // Arrange
        var grayscale = new byte[] { 0, 128, 255 };
        var page = MakeGrayscalePage(width: 3, height: 1, grayscale);

        // Act
        var rgba = page.GetRgbaPixelData();

        // Assert
        rgba.ShouldBe(new byte[]
        {
            255, 255, 255, 0,
            255, 255, 255, 128,
            255, 255, 255, 255
        });
    }

    [Fact]
    public void GetRgbaPixelData_FromRgbaPage_ReturnsClone()
    {
        // Arrange
        var rgbaSource = new byte[]
        {
            10, 20, 30, 40,
            50, 60, 70, 80
        };
        var page = MakeRgbaPage(width: 2, height: 1, rgbaSource);

        // Act
        var result = page.GetRgbaPixelData();

        // Assert
        result.ShouldBe(rgbaSource);
        ReferenceEquals(result, page.PixelData).ShouldBeFalse();
    }

    [Fact]
    public void GetRgbaPixelData_ResultLength_IsWidthTimesHeightTimesFour()
    {
        // Arrange
        var grayscalePage = MakeGrayscalePage(width: 4, height: 3, new byte[4 * 3]);
        var rgbaPage = MakeRgbaPage(width: 4, height: 3, new byte[4 * 3 * 4]);

        // Act
        var fromGrayscale = grayscalePage.GetRgbaPixelData();
        var fromRgba = rgbaPage.GetRgbaPixelData();

        // Assert
        fromGrayscale.Length.ShouldBe(4 * 3 * 4);
        fromRgba.Length.ShouldBe(4 * 3 * 4);
    }

    [Fact]
    public void GetRgbaPixelData_DoesNotMutateOriginalPixelData()
    {
        // Arrange
        var grayscaleSource = new byte[] { 10, 20, 30, 40 };
        var grayscalePage = MakeGrayscalePage(width: 4, height: 1, grayscaleSource);

        var rgbaSource = new byte[]
        {
            10, 20, 30, 40,
            50, 60, 70, 80
        };
        var rgbaPage = MakeRgbaPage(width: 2, height: 1, rgbaSource);

        // Act
        var rgbaFromGrayscale = grayscalePage.GetRgbaPixelData();
        for (int i = 0; i < rgbaFromGrayscale.Length; i++)
            rgbaFromGrayscale[i] = 0;

        var rgbaFromRgba = rgbaPage.GetRgbaPixelData();
        for (int i = 0; i < rgbaFromRgba.Length; i++)
            rgbaFromRgba[i] = 0;

        // Assert
        grayscalePage.PixelData.ShouldBe(new byte[] { 10, 20, 30, 40 });
        rgbaPage.PixelData.ShouldBe(new byte[]
        {
            10, 20, 30, 40,
            50, 60, 70, 80
        });
    }

    // ---------------------------------------------------------------
    // GetAlpha8PixelData
    // ---------------------------------------------------------------

    [Fact]
    public void GetAlpha8PixelData_FromRgbaPage_ExtractsAlphaChannel()
    {
        // Arrange
        var rgbaSource = new byte[]
        {
            10, 20, 30, 40,
            50, 60, 70, 80
        };
        var page = MakeRgbaPage(width: 2, height: 1, rgbaSource);

        // Act
        var alpha = page.GetAlpha8PixelData();

        // Assert
        alpha.ShouldBe(new byte[] { 40, 80 });
    }

    [Fact]
    public void GetAlpha8PixelData_FromGrayscalePage_ReturnsClone()
    {
        // Arrange
        var grayscaleSource = new byte[] { 0, 128, 255 };
        var page = MakeGrayscalePage(width: 3, height: 1, grayscaleSource);

        // Act
        var result = page.GetAlpha8PixelData();

        // Assert
        result.ShouldBe(grayscaleSource);
        ReferenceEquals(result, page.PixelData).ShouldBeFalse();
    }

    [Fact]
    public void GetAlpha8PixelData_ResultLength_IsWidthTimesHeight()
    {
        // Arrange
        var grayscalePage = MakeGrayscalePage(width: 4, height: 3, new byte[4 * 3]);
        var rgbaPage = MakeRgbaPage(width: 4, height: 3, new byte[4 * 3 * 4]);

        // Act
        var fromGrayscale = grayscalePage.GetAlpha8PixelData();
        var fromRgba = rgbaPage.GetAlpha8PixelData();

        // Assert
        fromGrayscale.Length.ShouldBe(4 * 3);
        fromRgba.Length.ShouldBe(4 * 3);
    }

    [Fact]
    public void GetAlpha8PixelData_DoesNotMutateOriginalPixelData()
    {
        // Arrange
        var grayscaleSource = new byte[] { 10, 20, 30, 40 };
        var grayscalePage = MakeGrayscalePage(width: 4, height: 1, grayscaleSource);

        var rgbaSource = new byte[]
        {
            10, 20, 30, 40,
            50, 60, 70, 80
        };
        var rgbaPage = MakeRgbaPage(width: 2, height: 1, rgbaSource);

        // Act
        var alphaFromGrayscale = grayscalePage.GetAlpha8PixelData();
        for (int i = 0; i < alphaFromGrayscale.Length; i++)
            alphaFromGrayscale[i] = 0;

        var alphaFromRgba = rgbaPage.GetAlpha8PixelData();
        for (int i = 0; i < alphaFromRgba.Length; i++)
            alphaFromRgba[i] = 0;

        // Assert
        grayscalePage.PixelData.ShouldBe(new byte[] { 10, 20, 30, 40 });
        rgbaPage.PixelData.ShouldBe(new byte[]
        {
            10, 20, 30, 40,
            50, 60, 70, 80
        });
    }

    // ---------------------------------------------------------------
    // GetPremultipliedRgbaPixelData
    // ---------------------------------------------------------------

    [Fact]
    public void GetPremultipliedRgbaPixelData_FromGrayscalePage_ExpandsToWhitePremultipliedByCoverage()
    {
        // Arrange
        var grayscale = new byte[] { 0, 128, 255 };
        var page = MakeGrayscalePage(width: 3, height: 1, grayscale);

        // Act
        var rgba = page.GetPremultipliedRgbaPixelData();

        // Assert — white coverage premultiplied by alpha → (v, v, v, v)
        rgba.ShouldBe(new byte[]
        {
            0, 0, 0, 0,
            128, 128, 128, 128,
            255, 255, 255, 255
        });
    }

    [Fact]
    public void GetPremultipliedRgbaPixelData_FromRgbaPage_PremultipliesEachChannel()
    {
        // Arrange — opaque, fully transparent, and half-alpha pixels
        var rgbaSource = new byte[]
        {
            200, 100,  50, 255, // opaque → unchanged
             80,  60,  40,   0, // transparent → zeroed
            200, 100,  50, 128  // half alpha → channels scaled by 128/255
        };
        var page = MakeRgbaPage(width: 3, height: 1, rgbaSource);

        // Act
        var rgba = page.GetPremultipliedRgbaPixelData();

        // Assert
        rgba.ShouldBe(new byte[]
        {
            200, 100, 50, 255,
            0,   0,   0,  0,
            (byte)(200 * 128 / 255), (byte)(100 * 128 / 255), (byte)(50 * 128 / 255), 128
        });
    }

    [Fact]
    public void GetPremultipliedRgbaPixelData_ResultLength_IsWidthTimesHeightTimesFour()
    {
        // Arrange
        var grayscalePage = MakeGrayscalePage(width: 4, height: 3, new byte[4 * 3]);
        var rgbaPage = MakeRgbaPage(width: 4, height: 3, new byte[4 * 3 * 4]);

        // Act
        var fromGrayscale = grayscalePage.GetPremultipliedRgbaPixelData();
        var fromRgba = rgbaPage.GetPremultipliedRgbaPixelData();

        // Assert
        fromGrayscale.Length.ShouldBe(4 * 3 * 4);
        fromRgba.Length.ShouldBe(4 * 3 * 4);
    }

    [Fact]
    public void GetPremultipliedRgbaPixelData_DoesNotMutateOriginalPixelData()
    {
        // Arrange
        var grayscaleSource = new byte[] { 10, 20, 30, 40 };
        var grayscalePage = MakeGrayscalePage(width: 4, height: 1, grayscaleSource);

        var rgbaSource = new byte[]
        {
            10, 20, 30, 40,
            50, 60, 70, 80
        };
        var rgbaPage = MakeRgbaPage(width: 2, height: 1, rgbaSource);

        // Act
        var fromGrayscale = grayscalePage.GetPremultipliedRgbaPixelData();
        for (int i = 0; i < fromGrayscale.Length; i++)
            fromGrayscale[i] = 0;

        var fromRgba = rgbaPage.GetPremultipliedRgbaPixelData();
        for (int i = 0; i < fromRgba.Length; i++)
            fromRgba[i] = 0;

        // Assert
        grayscalePage.PixelData.ShouldBe(new byte[] { 10, 20, 30, 40 });
        rgbaPage.PixelData.ShouldBe(new byte[]
        {
            10, 20, 30, 40,
            50, 60, 70, 80
        });
    }
}
