using KernSmith.Atlas;
using KernSmith.Font.Models;
using KernSmith.Output;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.FreeType;
using KernSmith.Rasterizers.StbTrueType;
using Shouldly;

namespace KernSmith.Tests;

/// <summary>
/// Phase 170 — the multi-channel distance-field surface in core: <see cref="SdfMode"/>
/// resolution against the legacy <c>Sdf</c> flag, the <c>SupportsMsdf</c> capability gate,
/// MSDF/MTSDF guards, RGBA output, <c>.fnt</c> channel/metadata flags, and <c>.bmfc</c> keys.
/// </summary>
[Collection("RasterizerFactory")]
public sealed class SdfModeTests
{
    private static readonly string FontPath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static byte[] Font() => File.ReadAllBytes(FontPath);

    private static FontGeneratorOptions Options(SdfMode mode, IRasterizer? rasterizer = null) => new()
    {
        Size = 24,
        Characters = CharacterSet.FromChars("AB"),
        SdfMode = mode,
        Rasterizer = rasterizer,
    };

    // ---- Mode resolution -------------------------------------------------------------------

    [Theory]
    [InlineData(false, SdfMode.None, SdfMode.None)]
    [InlineData(true, SdfMode.None, SdfMode.Sdf)]
    [InlineData(false, SdfMode.Sdf, SdfMode.Sdf)]
    [InlineData(false, SdfMode.Msdf, SdfMode.Msdf)]
    [InlineData(true, SdfMode.Msdf, SdfMode.Msdf)]
    [InlineData(true, SdfMode.Mtsdf, SdfMode.Mtsdf)]
    public void EffectiveSdfMode_PrefersSdfMode_AndFallsBackToTheSdfFlag(bool sdf, SdfMode mode, SdfMode expected)
    {
        new FontGeneratorOptions { Sdf = sdf, SdfMode = mode }.EffectiveSdfMode.ShouldBe(expected);
        new RasterOptions { Size = 12, Sdf = sdf, SdfMode = mode }.EffectiveSdfMode.ShouldBe(expected);
    }

    [Fact]
    public void Defaults_AreNone()
    {
        new FontGeneratorOptions().SdfMode.ShouldBe(SdfMode.None);
        var raster = RasterOptions.FromGeneratorOptions(new FontGeneratorOptions());
        raster.SdfMode.ShouldBe(SdfMode.None);
        raster.Sdf.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, SdfMode.Sdf, true)]
    [InlineData(true, SdfMode.None, true)]
    [InlineData(true, SdfMode.Msdf, false)]
    [InlineData(false, SdfMode.Mtsdf, false)]
    public void FromGeneratorOptions_SetsTheLegacyFlag_ExactlyForSingleChannelSdf(bool sdf, SdfMode mode, bool expectedFlag)
    {
        // Backends that predate SdfMode read only RasterOptions.Sdf: it must be on for every
        // single-channel request and off for MSDF, which they would otherwise render as SDF.
        var raster = RasterOptions.FromGeneratorOptions(new FontGeneratorOptions { Size = 12, Sdf = sdf, SdfMode = mode });

        raster.Sdf.ShouldBe(expectedFlag);
        raster.SdfMode.ShouldBe(new FontGeneratorOptions { Sdf = sdf, SdfMode = mode }.EffectiveSdfMode);
    }

    // ---- Capability gate -------------------------------------------------------------------

    [Fact]
    public void ExistingBackends_DeclineMsdf()
    {
        using var freeType = new FreeTypeRasterizer();
        using var stb = new StbTrueTypeRasterizer();
        freeType.Capabilities.SupportsMsdf.ShouldBeFalse();
        stb.Capabilities.SupportsMsdf.ShouldBeFalse();
        IRasterizerCapabilities minimal = new MinimalCaps();
        minimal.SupportsMsdf.ShouldBeFalse();
    }

    [Theory]
    [InlineData(SdfMode.Msdf)]
    [InlineData(SdfMode.Mtsdf)]
    public void Generate_OnABackendWithoutMsdf_IsRejected(SdfMode mode)
    {
        var options = Options(mode);
        options.Backend = RasterizerBackend.FreeType;

        Should.Throw<NotSupportedException>(() => BmFont.Generate(Font(), options))
            .Message.ShouldContain("MSDF", Case.Sensitive);
    }

    [Fact]
    public void SdfModeSdf_WithoutTheLegacyFlag_RendersExactlyLikeTheFlag()
    {
        var viaMode = BmFont.Generate(Font(), Options(SdfMode.Sdf));
        var legacy = Options(SdfMode.None);
        legacy.Sdf = true;
        var viaFlag = BmFont.Generate(Font(), legacy);

        viaMode.FntText.ShouldBe(viaFlag.FntText);
        viaMode.GetPngData()[0].ShouldBe(viaFlag.GetPngData()[0]);
        viaFlag.FntText.ShouldNotContain("sdfMode", Case.Sensitive);
    }

    // ---- Guards ----------------------------------------------------------------------------

    public static TheoryData<string> MsdfIncompatibilities => new()
    {
        "ChannelPacking", "Outline", "Gradient", "Shadow", "FillColor", "Channels", "SuperSample", "Variants", "ColorFont",
    };

    [Theory]
    [MemberData(nameof(MsdfIncompatibilities))]
    public void Msdf_RejectsOptionsThatTreatTheRgbAsColor(string incompatibility)
    {
        var options = Options(SdfMode.Msdf, new FakeMsdfRasterizer());
        switch (incompatibility)
        {
            case "ChannelPacking": options.ChannelPacking = true; break;
            case "Outline": options.Outline = 2; break;
            case "Gradient": options.GradientStartR = 255; options.GradientEndR = 0; break;
            case "Shadow": options.ShadowOffsetX = 2; options.ShadowOffsetY = 2; break;
            case "FillColor": options.FillColorR = 10; break;
            case "Channels": options.Channels = new ChannelConfig { Alpha = ChannelContent.Glyph, Red = ChannelContent.One, Green = ChannelContent.One, Blue = ChannelContent.One }; break;
            case "SuperSample": options.SuperSampleLevel = 2; break;
            case "Variants": options.Variants = [new AtlasVariant("shadow", AtlasVariantKind.ShadowSilhouette)]; break;
            case "ColorFont": options.ColorFont = true; break;
        }

        var ex = Should.Throw<Exception>(() => BmFont.Generate(Font(), options));
        (ex is InvalidOperationException or NotSupportedException).ShouldBeTrue(ex.GetType().Name + ": " + ex.Message);
    }

    // ---- Output ----------------------------------------------------------------------------

    [Fact]
    public void Msdf_ProducesAnRgbaAtlas_WithRgbGlyphChannelsAndAlphaOne()
    {
        var rasterizer = new FakeMsdfRasterizer();
        var result = BmFont.Generate(Font(), Options(SdfMode.Msdf, rasterizer));

        rasterizer.LastMode.ShouldBe(SdfMode.Msdf);
        result.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);

        // BMFont channel codes: 0 = glyph data, 4 = set to one.
        result.Model.Common.AlphaChnl.ShouldBe(4);
        result.Model.Common.RedChnl.ShouldBe(0);
        result.Model.Common.GreenChnl.ShouldBe(0);
        result.Model.Common.BlueChnl.ShouldBe(0);
        result.FntText.ShouldContain("sdfSpread=8", Case.Sensitive);
        result.FntText.ShouldContain("sdfMode=msdf", Case.Sensitive);
        result.Model.Extended!.SdfMode.ShouldBe(SdfMode.Msdf);
    }

    [Fact]
    public void Mtsdf_CarriesTheDistanceInAlpha()
    {
        var result = BmFont.Generate(Font(), Options(SdfMode.Mtsdf, new FakeMsdfRasterizer()));

        result.Pages[0].Format.ShouldBe(PixelFormat.Rgba32);
        result.Model.Common.AlphaChnl.ShouldBe(0);
        result.FntText.ShouldContain("sdfMode=mtsdf", Case.Sensitive);
    }

    [Fact]
    public void Mtsdf_SdfScaleDownsample_AveragesEachChannelOnItsOwn()
    {
        // Colour downsampling premultiplies by alpha; MTSDF alpha is a distance, so a texel far
        // outside (alpha 0) would lose its RGB distances. Each channel must average alone.
        var rasterizer = new FakeMsdfRasterizer(r: 200, g: 100, b: 50, a: 0);
        var options = Options(SdfMode.Mtsdf, rasterizer);
        options.SdfScale = 2;

        var result = BmFont.Generate(Font(), options);
        var a = result.Model.Characters.First(c => c.Id == 'A');
        var page = result.Pages[a.Page];
        int i = ((a.Y * page.Width) + a.X) * 4;

        page.PixelData[i].ShouldBe((byte)200);
        page.PixelData[i + 1].ShouldBe((byte)100);
        page.PixelData[i + 2].ShouldBe((byte)50);
        page.PixelData[i + 3].ShouldBe((byte)0);
    }

    [Theory]
    [InlineData(SdfMode.Msdf)]
    [InlineData(SdfMode.Mtsdf)]
    public void SdfModeMetadata_RoundTripsThroughEveryFntFormat(SdfMode mode)
    {
        var result = BmFont.Generate(Font(), Options(mode, new FakeMsdfRasterizer()));

        BmFontReader.ReadText(result.FntText).Extended!.SdfMode.ShouldBe(mode);
        BmFontReader.ReadXml(result.ToXml()).Extended!.SdfMode.ShouldBe(mode);
        BmFontReader.ReadBinary(result.ToBinary()).Extended!.SdfMode.ShouldBe(mode);
    }

    // ---- Config ----------------------------------------------------------------------------

    [Theory]
    [InlineData("msdf", SdfMode.Msdf)]
    [InlineData("mtsdf", SdfMode.Mtsdf)]
    [InlineData("sdf", SdfMode.Sdf)]
    [InlineData("MSDF", SdfMode.Msdf)]
    public void Bmfc_SdfModeKey_IsReadAndRoundTrips(string value, SdfMode expected)
    {
        var parsed = BmfcConfigReader.Parse($"fontSize=32\nsdfMode={value}\n");
        parsed.Options.SdfMode.ShouldBe(expected);

        var written = BmfcConfigWriter.Write(parsed);
        written.ShouldContain($"sdfMode={expected.ToString().ToLowerInvariant()}", Case.Sensitive);
        BmfcConfigReader.Parse(written).Options.SdfMode.ShouldBe(expected);
    }

    [Fact]
    public void Bmfc_DefaultConfig_HasNoSdfModeKey()
    {
        BmfcConfigWriter.Write(BmfcConfig.FromOptions(new FontGeneratorOptions()))
            .ShouldNotContain("sdfMode", Case.Sensitive);
    }

    [Fact]
    public void Builder_WithSdfMode_AndFromConfig_CarryTheMode()
    {
        var rasterizer = new FakeMsdfRasterizer();
        BmFont.Builder().WithFont(Font()).WithSize(24).WithCharacters(CharacterSet.FromChars("A"))
            .WithRasterizer(rasterizer).WithSdfMode(SdfMode.Mtsdf).Build();
        rasterizer.LastMode.ShouldBe(SdfMode.Mtsdf);

        var config = BmfcConfigReader.Parse($"fontFile={FontPath}\nfontSize=24\nchars=65\nsdfMode=msdf\n");
        var fromConfig = new FakeMsdfRasterizer();
        BmFont.Builder().FromConfig(config).WithRasterizer(fromConfig).Build();
        fromConfig.LastMode.ShouldBe(SdfMode.Msdf);
    }

    // ---- Test doubles ----------------------------------------------------------------------

    private sealed class MinimalCaps : IRasterizerCapabilities
    {
        public bool SupportsColorFonts => false;
        public bool SupportsVariableFonts => false;
        public bool SupportsSdf => false;
        public bool SupportsOutlineStroke => false;
        public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } = [AntiAliasMode.Grayscale];
    }

    /// <summary>An MSDF-capable backend that renders every glyph as a uniform 8x8 RGBA block.</summary>
    private sealed class FakeMsdfRasterizer(byte r = 200, byte g = 100, byte b = 50, byte a = 255) : IRasterizer
    {
        public SdfMode LastMode { get; private set; }

        public IRasterizerCapabilities Capabilities { get; } = new Caps();

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
        {
        }

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
        {
            LastMode = options.EffectiveSdfMode;
            var pixels = new byte[8 * 8 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = r;
                pixels[i + 1] = g;
                pixels[i + 2] = b;
                pixels[i + 3] = a;
            }

            return new RasterizedGlyph
            {
                Codepoint = codepoint,
                GlyphIndex = codepoint,
                BitmapData = pixels,
                Width = 8,
                Height = 8,
                Pitch = 32,
                Metrics = new GlyphMetrics(0, 8, 10, 8, 8),
                Format = PixelFormat.Rgba32,
            };
        }

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options) =>
            codepoints.Select(c => RasterizeGlyph(c, options)!).ToList();

        public void Dispose()
        {
        }

        private sealed class Caps : IRasterizerCapabilities
        {
            public bool SupportsColorFonts => false;
            public bool SupportsVariableFonts => false;
            public bool SupportsSdf => true;
            public bool SupportsMsdf => true;
            public bool SupportsOutlineStroke => false;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } = [AntiAliasMode.Grayscale];
        }
    }
}
