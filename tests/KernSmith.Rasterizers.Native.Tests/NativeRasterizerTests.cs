using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

public class NativeRasterizerTests
{
    private static NativeRasterizer LoadedRasterizer()
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.LoadFont(TestFonts.RobotoRegularBytes());
        return rasterizer;
    }

    [Fact]
    public void Capabilities_AreMinimal()
    {
        using var rasterizer = new NativeRasterizer();
        var caps = rasterizer.Capabilities;

        caps.SupportsColorFonts.ShouldBeFalse();
        caps.SupportsVariableFonts.ShouldBeFalse();
        caps.SupportsSdf.ShouldBeFalse();
        caps.SupportsOutlineStroke.ShouldBeFalse();
        caps.SupportedAntiAliasModes.ShouldBe([AntiAliasMode.None, AntiAliasMode.Grayscale]);
    }

    [Fact]
    public void LoadFont_ParsesFaceAndMapsGlyphs()
    {
        using var rasterizer = LoadedRasterizer();
        rasterizer.GetGlyphIndex('A').ShouldBeGreaterThan(0);
        rasterizer.Face.ShouldNotBeNull();
    }

    [Fact]
    public void LoadFont_Twice_Throws()
    {
        using var rasterizer = LoadedRasterizer();
        Should.Throw<InvalidOperationException>(() => rasterizer.LoadFont(TestFonts.RobotoRegularBytes()));
    }

    [Fact]
    public void LoadSystemFont_NotSupported()
    {
        using var rasterizer = new NativeRasterizer();
        Should.Throw<NotSupportedException>(() => rasterizer.LoadSystemFont("Arial"));
    }

    [Fact]
    public void RasterizeGlyph_IsStubbed()
    {
        using var rasterizer = LoadedRasterizer();
        Should.Throw<NotImplementedException>(() => rasterizer.RasterizeGlyph('A', new RasterOptions { Size = 32 }));
    }

    [Fact]
    public void RasterizeAll_IsStubbed()
    {
        using var rasterizer = LoadedRasterizer();
        Should.Throw<NotImplementedException>(() => rasterizer.RasterizeAll([65], new RasterOptions { Size = 32 }));
    }

    [Fact]
    public void RasterizeGlyph_WithoutFont_Throws()
    {
        using var rasterizer = new NativeRasterizer();
        Should.Throw<InvalidOperationException>(() => rasterizer.RasterizeGlyph('A', new RasterOptions { Size = 32 }));
    }

    [Fact]
    public void Dispose_ThenUse_Throws()
    {
        var rasterizer = new NativeRasterizer();
        rasterizer.Dispose();
        Should.Throw<ObjectDisposedException>(() => rasterizer.LoadFont(TestFonts.RobotoRegularBytes()));
    }

    [Fact]
    public void Factory_CanCreateNativeBackend()
    {
        // Reference the assembly type so the module initializer runs even under trimming-style loads.
        _ = typeof(NativeRasterizer);

        using var rasterizer = RasterizerFactory.Create(RasterizerBackend.Native);
        rasterizer.ShouldBeOfType<NativeRasterizer>();
    }
}
