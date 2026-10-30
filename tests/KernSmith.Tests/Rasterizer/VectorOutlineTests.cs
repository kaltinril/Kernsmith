using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using Shouldly;

namespace KernSmith.Tests.Rasterizer;

/// <summary>
/// Phase 168 — <see cref="OutlineMode.Vector"/>: a backend-drawn outline layer
/// (<see cref="IRasterizer.RasterizeOutlineLayer"/>) taking the place of core's bitmap
/// <c>OutlineEffect</c>. A stand-in backend wraps a real one and returns a known layer, so the
/// tests can tell exactly which pixels came from the layer.
/// </summary>
[Collection("RasterizerFactory")]
public class VectorOutlineTests
{
    private const int Ow = 2;

    private static byte[] Roboto() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Roboto-Regular.ttf"));

    private static FontGeneratorOptions Options(string chars = "HIlo", OutlineMode mode = OutlineMode.Vector) => new()
    {
        Size = 32,
        Characters = CharacterSet.FromChars(chars),
        Outline = Ow,
        OutlineR = 10,
        OutlineG = 20,
        OutlineB = 30,
        OutlineMode = mode,
    };

    private static RasterizationResult Rasterize(FontGeneratorOptions options, IRasterizer rasterizer)
    {
        options.Rasterizer = rasterizer;
        using var prepared = PreparedRasterization.Prepare(Roboto(), options, systemFontName: null, subsetToCharacterSet: true);
        return prepared.RasterizeAndProcess(prepared.ResolveCodepoints(), equalizeTargetHeight: null);
    }

    private static RasterizedGlyph Glyph(RasterizationResult result, char c) => result.Glyphs.Single(g => g.Codepoint == c);

    private static byte AlphaAt(RasterizedGlyph g, int x, int y) => g.BitmapData[(y * g.Pitch) + (x * 4) + 3];

    // ---- Opt-in -----------------------------------------------------------------------------

    [Fact]
    public void EffectMode_NeverAsksTheBackendForALayer()
    {
        using var backend = LayerBackend.Box();
        var result = Rasterize(Options(mode: OutlineMode.Effect), backend);

        backend.Calls.ShouldBeEmpty("the default Effect mode must not change how any backend is driven");
        result.Glyphs.ShouldNotBeEmpty();
    }

    [Fact]
    public void VectorMode_BackendWithoutTheCapability_IsNotAskedAndMatchesTheEffect()
    {
        using var backend = LayerBackend.Box(supportsStroke: false);
        var vector = Rasterize(Options(), backend);
        using var plain = LayerBackend.Box(supportsStroke: false);
        var effect = Rasterize(Options(mode: OutlineMode.Effect), plain);

        backend.Calls.ShouldBeEmpty();
        AssertSameGlyphs(vector, effect);
    }

    [Fact]
    public void VectorMode_BackendReturnsNoLayer_MatchesTheEffectExactly()
    {
        using var backend = new LayerBackend((_, _, _) => null);
        var vector = Rasterize(Options(), backend);
        using var plain = new LayerBackend((_, _, _) => null);
        var effect = Rasterize(Options(mode: OutlineMode.Effect), plain);

        backend.Calls.Count.ShouldBe(4, "Vector mode asks once per glyph");
        AssertSameGlyphs(vector, effect);
    }

    // ---- Composition ------------------------------------------------------------------------

    [Fact]
    public void VectorMode_CompositesTheLayer_InTheOutlineColor_BehindTheBody()
    {
        using var backend = LayerBackend.Box();
        var vector = Rasterize(Options(), backend);
        using var plain = LayerBackend.Box();
        var effect = Rasterize(Options(mode: OutlineMode.Effect), plain);

        var v = Glyph(vector, 'H');
        var e = Glyph(effect, 'H');

        // The box layer is exactly the body box grown by the width, like the effect's canvas.
        v.Width.ShouldBe(e.Width);
        v.Height.ShouldBe(e.Height);
        v.Metrics.BearingX.ShouldBe(e.Metrics.BearingX);
        v.Metrics.BearingY.ShouldBe(e.Metrics.BearingY);
        v.Metrics.Advance.ShouldBe(e.Metrics.Advance);

        // The effect rounds the canvas corner off; the box layer fills it, in the outline color.
        AlphaAt(e, 0, 0).ShouldBeLessThan((byte)255);
        AlphaAt(v, 0, 0).ShouldBe((byte)255, "the corner pixel comes from the backend's layer");
        v.BitmapData[0].ShouldBe((byte)10);
        v.BitmapData[1].ShouldBe((byte)20);
        v.BitmapData[2].ShouldBe((byte)30);

        // The body still composites on top: the H's crossbar centre is white.
        int cx = v.Width / 2, cy = v.Height / 2;
        v.BitmapData[(cy * v.Pitch) + (cx * 4)].ShouldBe((byte)255);
    }

    [Fact]
    public void VectorMode_LayerReachingPastTheEffectRing_IsClippedToTheEffectsCell()
    {
        // Two extra pixels on the left only: a layer never changes the cell.
        using var backend = LayerBackend.Box(extraLeft: 2);
        var vector = Rasterize(Options(), backend);
        using var plain = LayerBackend.Box();
        var effect = Rasterize(Options(mode: OutlineMode.Effect), plain);

        var v = Glyph(vector, 'H');
        var e = Glyph(effect, 'H');
        (v.Width, v.Height, v.Metrics.BearingX, v.Metrics.BearingY)
            .ShouldBe((e.Width, e.Height, e.Metrics.BearingX, e.Metrics.BearingY));
        AlphaAt(v, 0, 0).ShouldBe((byte)255, "the layer still fills the cell it shares with the effect");
    }

    [Fact]
    public void VectorMode_AsksSeriallyWithTheRenderSizeWidthAndShape()
    {
        using var backend = LayerBackend.Box();
        var options = Options("ABCDEFGHIJKLMNOP");
        options.SuperSampleLevel = 2;
        options.OutlineShape = OutlineShape.Square;
        Rasterize(options, backend);

        backend.Calls.Count.ShouldBe(16);
        backend.MaxConcurrentCalls.ShouldBe(1, "rasterizers are not thread-safe; layers must be asked for in the serial step");
        backend.Calls.ShouldAllBe(c => c.Width == Ow && c.Shape == OutlineShape.Square && c.HeightScale.Equals(1f));
        // Same thickness definition as the effect: Outline pixels at the supersampled render size.
        backend.Calls.ShouldAllBe(c => c.Size.Equals(backend.GlyphSizes[0]));
        backend.GlyphSizes.ShouldAllBe(s => s.Equals(backend.GlyphSizes[0]));
    }

    [Fact]
    public void VectorMode_CustomGlyph_KeepsTheEffect()
    {
        var custom = new CustomGlyph(6, 6, Enumerable.Repeat((byte)255, 36).ToArray(), PixelFormat.Grayscale8);
        var vectorOptions = Options();
        vectorOptions.CustomGlyphs = new Dictionary<int, CustomGlyph> { ['H'] = custom };
        var effectOptions = Options(mode: OutlineMode.Effect);
        effectOptions.CustomGlyphs = new Dictionary<int, CustomGlyph> { ['H'] = custom };

        using var backend = LayerBackend.Box();
        var vector = Rasterize(vectorOptions, backend);
        using var plain = LayerBackend.Box();
        var effect = Rasterize(effectOptions, plain);

        var v = Glyph(vector, 'H');
        var e = Glyph(effect, 'H');
        v.BitmapData.ShouldBe(e.BitmapData, "a custom image has no outline for the backend to stroke");
        v.Metrics.ShouldBe(e.Metrics);
        AlphaAt(Glyph(vector, 'I'), 0, 0).ShouldBe((byte)255, "the other glyphs still use the layer");
    }

    [Theory]
    [InlineData(SdfMode.Sdf)]
    [InlineData(SdfMode.Msdf)]
    public void VectorMode_WithADistanceField_IsRejected(SdfMode mode)
    {
        using var backend = LayerBackend.Box();
        var options = Options();
        options.SdfMode = mode;
        options.Rasterizer = backend;

        var ex = Should.Throw<InvalidOperationException>(() =>
            PreparedRasterization.Prepare(Roboto(), options, systemFontName: null, subsetToCharacterSet: true));
        ex.Message.ShouldContain(mode == SdfMode.Sdf ? "Vector outline" : "MSDF", Case.Sensitive);
    }

    [Fact]
    public void VectorMode_WithoutAnOutline_IgnoresTheMode()
    {
        using var backend = LayerBackend.Box();
        var options = Options();
        options.Outline = 0;
        options.Sdf = true;
        options.Rasterizer = backend;

        using var prepared = PreparedRasterization.Prepare(Roboto(), options, systemFontName: null, subsetToCharacterSet: true);
        prepared.RasterizeAndProcess(prepared.ResolveCodepoints(), equalizeTargetHeight: null);
        backend.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void VectorMode_Shadow_FollowsTheLayersSilhouette()
    {
        static FontGeneratorOptions WithShadow(OutlineMode mode)
        {
            var o = Options("H", mode);
            o.ShadowOffsetX = 3;
            o.ShadowOffsetY = 3;
            o.HardShadow = true;
            return o;
        }

        using var backend = LayerBackend.Box();
        var v = Glyph(Rasterize(WithShadow(OutlineMode.Vector), backend), 'H');
        using var plain = LayerBackend.Box();
        var e = Glyph(Rasterize(WithShadow(OutlineMode.Effect), plain), 'H');

        v.Width.ShouldBe(e.Width);
        v.Height.ShouldBe(e.Height);
        // Bottom-right canvas corner: only the shadow of the layer's square corner reaches it.
        AlphaAt(e, e.Width - 1, e.Height - 1).ShouldBeLessThan((byte)255);
        AlphaAt(v, v.Width - 1, v.Height - 1).ShouldBe((byte)255, "the shadow is cast by the outlined silhouette");
    }

    [Fact]
    public void VectorMode_HeightStretch_PassesTheScale_AndKeepsTheRingCentredOnTheStretchedBody()
    {
        static FontGeneratorOptions Stretched(OutlineMode mode)
        {
            var o = Options("H", mode);
            o.HeightPercent = 150;
            return o;
        }

        using var backend = LayerBackend.Box(honorHeightScale: true);
        var v = Glyph(Rasterize(Stretched(OutlineMode.Vector), backend), 'H');
        using var plain = LayerBackend.Box();
        var e = Glyph(Rasterize(Stretched(OutlineMode.Effect), plain), 'H');

        backend.Calls.ShouldNotBeEmpty();
        backend.Calls.ShouldAllBe(c => c.HeightScale.Equals(1.5f));
        // The layer is drawn Ow / 1.5 px tall, so once stretched it is Ow all round — the
        // effect's canvas, and the same body placement.
        v.Width.ShouldBe(e.Width);
        v.Height.ShouldBe(e.Height);
        v.Metrics.BearingX.ShouldBe(e.Metrics.BearingX);
        v.Metrics.BearingY.ShouldBe(e.Metrics.BearingY);
        // The square layer reaches the canvas corners the round effect leaves empty; its outer
        // rows are resampled like the body's own rows, so they need not be solid.
        AlphaAt(e, 0, 0).ShouldBe((byte)0);
        AlphaAt(v, 0, 0).ShouldBeGreaterThan((byte)0);
        AlphaAt(v, v.Width - 1, v.Height - 1).ShouldBeGreaterThan((byte)0);
        AlphaAt(v, 0, v.Height / 2).ShouldBe((byte)255, "the layer's side columns are not resampled vertically into a fade");
    }

    private static FontGeneratorOptions Routed(OutlineMode mode, IRasterizer rasterizer, int superSample = 1)
    {
        var o = Options("H", mode);
        o.Channels = new ChannelConfig(
            Alpha: ChannelContent.Outline, Red: ChannelContent.Glyph, Green: ChannelContent.Glyph, Blue: ChannelContent.Glyph);
        o.SuperSampleLevel = superSample;
        o.Rasterizer = rasterizer;
        return o;
    }

    [Fact]
    public void VectorMode_ChannelRoutedOutline_SuperSampled_AsksForTheOutputWidthAndKeepsTheBodyInPlace()
    {
        using var backend = LayerBackend.Box();
        var vector = BmFont.Generate(Roboto(), Routed(OutlineMode.Vector, backend, superSample: 2));
        using var plain = LayerBackend.Box();
        var effect = BmFont.Generate(Roboto(), Routed(OutlineMode.Effect, plain, superSample: 2));

        // Routed outlines are Outline px at the output size, so the layer is asked for at
        // Outline × the supersample factor and box-filtered down with the body.
        backend.Calls.ShouldNotBeEmpty();
        backend.Calls.ShouldAllBe(c => c.Width == Ow * 2);

        var vc = vector.Model.Characters.Single(c => c.Id == 'H');
        var ec = effect.Model.Characters.Single(c => c.Id == 'H');
        vc.XOffset.ShouldBe(ec.XOffset);
        vc.YOffset.ShouldBe(ec.YOffset);
        vc.XAdvance.ShouldBe(ec.XAdvance);
        vc.Width.ShouldBe(ec.Width);
        vc.Height.ShouldBe(ec.Height);
    }

    [Fact]
    public void VectorMode_ChannelRoutedOutline_ComesFromTheLayer()
    {
        using var backend = LayerBackend.Box();
        var vector = BmFont.Generate(Roboto(), Routed(OutlineMode.Vector, backend));
        using var plain = LayerBackend.Box();
        var effect = BmFont.Generate(Roboto(), Routed(OutlineMode.Effect, plain));

        backend.Calls.ShouldNotBeEmpty();
        backend.Calls.ShouldAllBe(c => c.Width == Ow);

        var vc = vector.Model.Characters.Single(c => c.Id == 'H');
        var ec = effect.Model.Characters.Single(c => c.Id == 'H');
        vc.Width.ShouldBe(ec.Width);
        vc.Height.ShouldBe(ec.Height);
        vc.XOffset.ShouldBe(ec.XOffset);
        vc.YOffset.ShouldBe(ec.YOffset);

        static byte CornerAlpha(KernSmith.Output.BmFontResult r, KernSmith.Output.Model.CharEntry c)
        {
            var page = r.Pages[c.Page];
            return page.PixelData[((c.Y * page.Width) + c.X) * 4 + 3];
        }

        CornerAlpha(effect, ec).ShouldBeLessThan((byte)255);
        CornerAlpha(vector, vc).ShouldBe((byte)255, "the outline channel carries the backend's layer");
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static void AssertSameGlyphs(RasterizationResult actual, RasterizationResult expected)
    {
        actual.Glyphs.Count.ShouldBe(expected.Glyphs.Count);
        for (int i = 0; i < expected.Glyphs.Count; i++)
        {
            var a = actual.Glyphs[i];
            var e = expected.Glyphs[i];
            a.Codepoint.ShouldBe(e.Codepoint);
            a.Metrics.ShouldBe(e.Metrics);
            a.BitmapData.ShouldBe(e.BitmapData, $"U+{e.Codepoint:X4} pixels");
        }
    }

    /// <summary>One <see cref="IRasterizer.RasterizeOutlineLayer"/> call.</summary>
    internal sealed record LayerCall(int Codepoint, float Size, int Width, OutlineShape Shape, float HeightScale);

    /// <summary>
    /// The FreeType backend plus a scripted outline layer. <see cref="Box"/> returns an opaque
    /// rectangle over the glyph's box grown by the requested width, which no bitmap outline can
    /// produce (its corners are rounded), so a test can see where the layer was used.
    /// </summary>
    internal sealed class LayerBackend : IRasterizer
    {
        private readonly IRasterizer _inner = RasterizerFactory.Create(RasterizerBackend.FreeType);
        private readonly Func<RasterizedGlyph, int, float, RasterizedGlyph?> _layer;
        private readonly Caps _caps;
        private int _active;

        public LayerBackend(Func<RasterizedGlyph, int, float, RasterizedGlyph?> layer, bool supportsStroke = true)
        {
            _layer = layer;
            _caps = new Caps(_inner.Capabilities, supportsStroke);
        }

        public static LayerBackend Box(bool supportsStroke = true, int extraLeft = 0, bool honorHeightScale = false) =>
            new((glyph, width, heightScale) =>
            {
                int grow = width;
                int growY = honorHeightScale ? (int)MathF.Round(width / heightScale) : width;
                int w = glyph.Width + (2 * grow) + extraLeft;
                int h = glyph.Height + (2 * growY);
                return new RasterizedGlyph
                {
                    Codepoint = glyph.Codepoint,
                    GlyphIndex = glyph.GlyphIndex,
                    BitmapData = Enumerable.Repeat((byte)255, w * h).ToArray(),
                    Width = w,
                    Height = h,
                    Pitch = w,
                    Metrics = new GlyphMetrics(glyph.Metrics.BearingX - grow - extraLeft, glyph.Metrics.BearingY + growY, glyph.Metrics.Advance, w, h),
                    Format = PixelFormat.Grayscale8,
                };
            }, supportsStroke);

        public List<LayerCall> Calls { get; } = [];

        public List<float> GlyphSizes { get; } = [];

        public int MaxConcurrentCalls { get; private set; }

        public IRasterizerCapabilities Capabilities => _caps;

        public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0) => _inner.LoadFont(fontData, faceIndex);

        public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
        {
            GlyphSizes.Add(options.Size);
            return _inner.RasterizeGlyph(codepoint, options);
        }

        public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options) =>
            codepoints.Select(cp => RasterizeGlyph(cp, options)).OfType<RasterizedGlyph>().ToList();

        public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options) => _inner.GetGlyphMetrics(codepoint, options);

        public RasterizerFontMetrics? GetFontMetrics(RasterOptions options) => _inner.GetFontMetrics(options);

        public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options) => _inner.GetKerningPairs(options);

        public void SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes) => _inner.SetVariationAxes(fvarAxes, userAxes);

        public RasterizedGlyph? RasterizeOutlineLayer(int codepoint, RasterOptions options, int width, OutlineShape shape, float heightScale = 1f)
        {
            int active = Interlocked.Increment(ref _active);
            try
            {
                lock (Calls)
                {
                    MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, active);
                    Calls.Add(new LayerCall(codepoint, options.Size, width, shape, heightScale));
                }

                Thread.Sleep(1);
                var glyph = _inner.RasterizeGlyph(codepoint, options);
                return glyph is null || glyph.Width == 0 ? null : _layer(glyph, width, heightScale);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Dispose() => _inner.Dispose();

        private sealed class Caps(IRasterizerCapabilities inner, bool supportsStroke) : IRasterizerCapabilities
        {
            public bool SupportsColorFonts => inner.SupportsColorFonts;
            public bool SupportsVariableFonts => inner.SupportsVariableFonts;
            public bool SupportsSdf => inner.SupportsSdf;
            public bool SupportsOutlineStroke => supportsStroke;
            public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes => inner.SupportedAntiAliasModes;
            public bool HandlesOwnSizing => inner.HandlesOwnSizing;
            public bool SupportsSyntheticBold => inner.SupportsSyntheticBold;
            public bool SupportsSyntheticItalic => inner.SupportsSyntheticItalic;
        }
    }
}
