using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

public class RasterizerFactoryTests
{
    [Fact]
    public void Create_FreeType_ReturnsFreeTypeRasterizer()
    {
        using var rasterizer = RasterizerFactory.Create(RasterizerBackend.FreeType);

        rasterizer.ShouldNotBeNull();
    }

    [Fact]
    public void GetAvailableBackends_IncludesFreeType()
    {
        var backends = RasterizerFactory.GetAvailableBackends();

        backends.ShouldContain(RasterizerBackend.FreeType);
    }

    [Fact]
    public void Create_UnregisteredBackend_ThrowsInvalidOperationException()
    {
        var act = () => RasterizerFactory.Create(RasterizerBackend.Gdi);

        var ex = Should.Throw<InvalidOperationException>(act);
        ex.Message.ShouldContain("Gdi");
    }

    [Fact]
    public void Register_ThenCreate_ReturnsRegisteredInstance()
    {
        var called = false;
        RasterizerFactory.Register(RasterizerBackend.DirectWrite, () =>
        {
            called = true;
            return RasterizerFactory.Create(RasterizerBackend.FreeType);
        });

        using var rasterizer = RasterizerFactory.Create(RasterizerBackend.DirectWrite);

        called.ShouldBeTrue();
        rasterizer.ShouldNotBeNull();
    }

    [Fact]
    public void FreeTypeRasterizer_Capabilities_ReportsCorrectValues()
    {
        using var rasterizer = RasterizerFactory.Create(RasterizerBackend.FreeType);

        var caps = rasterizer.Capabilities;

        caps.SupportsColorFonts.ShouldBeTrue();
        caps.SupportsVariableFonts.ShouldBeTrue();
        caps.SupportsSdf.ShouldBeTrue();
        caps.SupportsOutlineStroke.ShouldBeTrue();
        caps.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.None);
        caps.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.Grayscale);
        caps.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.Light);
        caps.SupportedAntiAliasModes.ShouldContain(AntiAliasMode.Lcd);
    }

    [Fact]
    public void FontGeneratorOptions_Backend_DefaultsToFreeType()
    {
        var options = new FontGeneratorOptions();

        options.Backend.ShouldBe(RasterizerBackend.FreeType);
    }
}
