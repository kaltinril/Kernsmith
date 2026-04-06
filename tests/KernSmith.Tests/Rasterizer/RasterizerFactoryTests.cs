using System.Collections.Concurrent;
using KernSmith.Rasterizer;
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
        // Use a backend value with no known assembly — auto-discovery can't find it
        var ex = Should.Throw<InvalidOperationException>(
            () => RasterizerFactory.Create((RasterizerBackend)999));
        ex.Message.ShouldContain("is not registered");
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

    [Fact]
    public void GetAvailableBackends_AfterReset_TriggersDiscovery()
    {
        try
        {
            RasterizerFactory.ResetForTesting();

            // GetAvailableBackends() should trigger auto-discovery without any prior Create() call
            var backends = RasterizerFactory.GetAvailableBackends();

            backends.ShouldNotBeEmpty();
            backends.ShouldContain(RasterizerBackend.FreeType);
        }
        finally
        {
            RasterizerFactory.ResetForTesting();
        }
    }

    [Fact]
    public async Task Create_ConcurrentCallsDuringDiscovery_DoNotThrow()
    {
        try
        {
            RasterizerFactory.ResetForTesting();

            var exceptions = new ConcurrentBag<Exception>();
            var barrier = new Barrier(10);

            var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    using var rasterizer = RasterizerFactory.Create(RasterizerBackend.FreeType);
                    rasterizer.ShouldNotBeNull();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            exceptions.ShouldBeEmpty($"Concurrent Create() calls threw: {string.Join("; ", exceptions.Select(e => e.Message))}");
        }
        finally
        {
            RasterizerFactory.ResetForTesting();
        }
    }
}
