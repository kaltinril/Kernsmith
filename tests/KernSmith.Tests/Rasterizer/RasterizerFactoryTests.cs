using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

[Collection("RasterizerFactory")]
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
        // Reset to empty state so no backends are registered.
        RasterizerFactory.ResetForTesting();
        try
        {
            var act = () => RasterizerFactory.Create(RasterizerBackend.DirectWrite);

            var ex = Should.Throw<InvalidOperationException>(act);
            ex.Message.ShouldContain("DirectWrite");
        }
        finally
        {
            // Re-register FreeType since ResetForTesting() clears all backends
            // and [ModuleInitializer] only fires once per assembly load.
            FreeTypeRegistration.Register();
        }
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
