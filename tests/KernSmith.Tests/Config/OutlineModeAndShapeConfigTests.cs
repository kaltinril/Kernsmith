using KernSmith.Tests.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Config;

/// <summary>
/// Phase 168 — the <c>.bmfc</c> keys <c>outlineMode</c>, <c>widthPercent</c> and <c>italicAngle</c>,
/// and <see cref="BmFontBuilder.FromConfig(BmfcConfig)"/> (the harness's path) carrying them.
/// </summary>
[Collection("RasterizerFactory")]
public sealed class OutlineModeAndShapeConfigTests
{
    private static byte[] Roboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    [Fact]
    public void Parse_ReadsTheKeys()
    {
        var options = BmfcConfigReader.Parse("outlineMode=vector\nwidthPercent=80\nitalicAngle=-7.5\n").Options;

        options.OutlineMode.ShouldBe(OutlineMode.Vector);
        options.WidthPercent.ShouldBe(80);
        options.ItalicAngle.ShouldBe(-7.5f);
    }

    [Fact]
    public void Parse_UnknownOutlineMode_KeepsTheEffect()
    {
        BmfcConfigReader.Parse("outlineMode=hologram\n").Options.OutlineMode.ShouldBe(OutlineMode.Effect);
    }

    [Fact]
    public void WriteThenParse_RoundTrips()
    {
        var config = BmfcConfig.FromOptions(new FontGeneratorOptions
        {
            Outline = 2,
            OutlineMode = OutlineMode.Vector,
            WidthPercent = 125,
            ItalicAngle = 20f,
        });

        var text = BmfcConfigWriter.Write(config);
        text.ShouldContain("outlineMode=vector", Case.Sensitive);
        text.ShouldContain("widthPercent=125", Case.Sensitive);
        text.ShouldContain("italicAngle=20", Case.Sensitive);

        var options = BmfcConfigReader.Parse(text).Options;
        options.OutlineMode.ShouldBe(OutlineMode.Vector);
        options.WidthPercent.ShouldBe(125);
        options.ItalicAngle.ShouldBe(20f);
    }

    [Fact]
    public void Write_Defaults_EmitNothing()
    {
        var text = BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions { Outline = 2 }));

        text.ShouldNotContain("outlineMode", Case.Sensitive);
        text.ShouldNotContain("widthPercent", Case.Sensitive);
        text.ShouldNotContain("italicAngle", Case.Sensitive);
    }

    [Fact]
    public void BuilderFromConfig_CarriesTheOutlineMode()
    {
        var config = BmfcConfigReader.Parse("fontSize=32\noutlineThickness=2\noutlineMode=vector\nchars=72\n");
        using var backend = VectorOutlineTests.LayerBackend.Box();

        BmFont.Builder().WithFont(Roboto()).FromConfig(config).WithRasterizer(backend).Build();

        backend.Calls.ShouldNotBeEmpty("outlineMode=vector must reach the pipeline");
    }

    [Fact]
    public void BuilderFromConfig_CarriesWidthAndAngle()
    {
        // FreeType declines both, which proves the values reached the pipeline.
        var width = BmfcConfigReader.Parse("fontSize=32\nwidthPercent=80\nchars=72\n");
        Should.Throw<NotSupportedException>(() => BmFont.Builder().WithFont(Roboto()).FromConfig(width).Build())
            .Message.ShouldContain("WidthPercent = 80", Case.Sensitive);

        var angle = BmfcConfigReader.Parse("fontSize=32\nisItalic=1\nitalicAngle=20\nchars=72\n");
        Should.Throw<NotSupportedException>(() => BmFont.Builder().WithFont(Roboto()).FromConfig(angle).Build())
            .Message.ShouldContain("ItalicAngle = 20", Case.Sensitive);
    }
}
