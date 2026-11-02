using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Pipeline;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Describes what the native rasterizer backend supports: TrueType (<c>glyf</c>) and CFF/CFF2
/// outlines with grayscale, thresholded or LCD subpixel (173) coverage, including
/// variable-font instances of both (171), color glyphs from <c>COLR</c> version 0 layers,
/// version 1 paint graphs and <c>CBDT</c>/<c>sbix</c> bitmaps (172), synthetic bold/italic as outline transforms (167),
/// single-channel signed distance fields (169), multi-channel MSDF/MTSDF (170), and vector
/// outline layers, synthetic width and italic angle (168).
/// </summary>
internal sealed class NativeCapabilities : IRasterizerCapabilities
{
    /// <summary>
    /// True: <c>COLR</c> version 0 layers and version 1 paint graphs with <c>CPAL</c> palettes,
    /// and <c>CBDT</c>/<c>sbix</c> PNG bitmaps scaled from the nearest strike.
    /// </summary>
    public bool SupportsColorFonts => true;
    public bool SupportsVariableFonts => true;
    public bool SupportsSdf => true;
    /// <summary>True: <see cref="NativeRasterizer.RasterizeOutlineLayer"/> strokes the vector outline.</summary>
    public bool SupportsOutlineStroke => true;
    public bool SupportsSystemFonts => false;
    public bool SupportsSyntheticBold => true;
    public bool SupportsSyntheticItalic => true;
    public bool SupportsMsdf => true;
    public bool SupportsWidthScale => true;
    public bool SupportsItalicAngle => true;

    /// <summary>
    /// False: the pipeline converts the requested BMFont cell height into an em size before
    /// calling in, so <see cref="RasterOptions.Size"/> arrives ready to use.
    /// </summary>
    public bool HandlesOwnSizing => false;

    /// <summary>
    /// <see cref="AntiAliasMode.Light"/> is grayscale with Native's light (vertical-only)
    /// auto-hinting when <see cref="RasterOptions.EnableHinting"/> is on (Phase 174).
    /// </summary>
    public IReadOnlyList<AntiAliasMode> SupportedAntiAliasModes { get; } =
        [AntiAliasMode.None, AntiAliasMode.Grayscale, AntiAliasMode.Light, AntiAliasMode.Lcd];

    /// <summary>
    /// <see cref="HintLevel.None"/> is unhinted and <see cref="HintLevel.Light"/> the y-only
    /// auto-hinter (Phase 174), whatever the anti-alias mode; <see cref="HintLevel.Normal"/> the
    /// both-axis auto-hinter with rebuilt advances (Phase 188), for grayscale and mono.
    /// <see cref="HintLevel.Full"/> is not listed — the pipeline steps it down to Normal — until
    /// a font-native engine (TrueType bytecode, CFF hints) exists.
    /// <see cref="GlyphHinter.EffectiveHintLevel"/> is the single place that maps a request to
    /// the level Native runs.
    /// </summary>
    public IReadOnlyList<HintLevel> SupportedHintLevels { get; } = [HintLevel.None, HintLevel.Light, HintLevel.Normal];
}
