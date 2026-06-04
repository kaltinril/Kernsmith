using Shouldly;

namespace KernSmith.Tests.Config;

public sealed class BatchConfigTests
{
    [Fact]
    public void BatchOptions_Defaults_AreSensible()
    {
        // Act
        var options = new BatchOptions();

        // Assert
        options.MaxParallelism.ShouldBe(1);
        options.FontCache.ShouldBeNull();
        options.AtlasMode.ShouldBe(BatchAtlasMode.Separate);
        options.CombinedMaxTextureWidth.ShouldBe(4096);
        options.CombinedMaxTextureHeight.ShouldBe(4096);
    }

    [Fact]
    public void BatchOptions_InitProperties_AreSettable()
    {
        // Act
        var options = new BatchOptions
        {
            MaxParallelism = 4,
            AtlasMode = BatchAtlasMode.Combined,
            CombinedMaxTextureWidth = 2048,
            CombinedMaxTextureHeight = 1024,
            FontCache = new FontCache(),
        };

        // Assert
        options.MaxParallelism.ShouldBe(4);
        options.AtlasMode.ShouldBe(BatchAtlasMode.Combined);
        options.CombinedMaxTextureWidth.ShouldBe(2048);
        options.CombinedMaxTextureHeight.ShouldBe(1024);
        options.FontCache.ShouldNotBeNull();
    }

    [Fact]
    public void BatchJob_Defaults_FontSourcesAreNull()
    {
        // Act
        var job = new BatchJob { Options = new FontGeneratorOptions() };

        // Assert
        job.FontData.ShouldBeNull();
        job.FontPath.ShouldBeNull();
        job.SystemFont.ShouldBeNull();
        job.Options.ShouldNotBeNull();
    }

    [Fact]
    public void BatchJob_StoresFontSource()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3 };

        // Act
        var job = new BatchJob
        {
            FontData = data,
            Options = new FontGeneratorOptions { Size = 16 },
        };

        // Assert
        job.FontData.ShouldBeSameAs(data);
        job.Options.Size.ShouldBe(16f);
    }
}
