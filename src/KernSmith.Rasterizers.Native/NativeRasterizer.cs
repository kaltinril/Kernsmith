using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Pipeline;
using KernSmith.Rasterizers.Native.Internal.Variations;

namespace KernSmith.Rasterizers.Native;

/// <summary>
/// Fully custom, pure C# rasterizer backend owned entirely by KernSmith. Zero external
/// dependencies. Cross-platform and WASM/AOT-friendly.
/// </summary>
/// <remarks>
/// Renders TrueType (<c>glyf</c>) and PostScript (<c>CFF </c>/<c>CFF2</c>, typically
/// <c>.otf</c>) outlines: the font is parsed in-house, outlines are flattened to line segments
/// and filled with a signed-area scanline rasterizer. A CFF2 variable font renders its default
/// instance.
///
/// TrueType variable fonts (Phase 171) render at the instance <see cref="SetVariationAxes"/>
/// selects: <c>gvar</c> outlines, <c>HVAR</c> (or phantom-point) advances and <c>MVAR</c> font
/// metrics. Synthetic bold/italic (Phase 167), outline stroking (Phase 168), SDF (Phase 169) and
/// color fonts (Phase 172) are not implemented; the corresponding
/// <see cref="IRasterizerCapabilities"/> flags are false. <see cref="RasterOptions.Bold"/>
/// and <see cref="RasterOptions.Italic"/> are ignored rather than rejected, so a font
/// configuration that works on other backends still generates here — just without the styling.
/// <see cref="RasterOptions.SuperSample"/> is likewise ignored: with
/// <see cref="IRasterizerCapabilities.HandlesOwnSizing"/> false the pipeline already renders
/// at the supersampled size and box-filters the result back down, exactly as it does for the
/// FreeType backend.
///
/// Each glyph runs a staged pipeline (see the package README): decode via an
/// <see cref="IOutlineDecoder"/>, outline transforms, bounds, scale/hint, then render-mode
/// dispatch. <see cref="GetGlyphMetrics"/> runs the same stages as
/// <see cref="RasterizeGlyph"/>, so metrics and bitmaps cannot disagree.
///
/// Instances are NOT thread-safe (Phase 160, D9). Create one instance per thread for
/// parallel rasterization.
/// </remarks>
public sealed class NativeRasterizer : IRasterizer
{
    private static readonly IRasterizerCapabilities CapabilitiesInstance = new NativeCapabilities();

    private NativeFontFace? _face;
    private GlyphPipeline? _pipeline;
    private FontVariations? _variations;
    private bool _disposed;

    /// <inheritdoc />
    public IRasterizerCapabilities Capabilities => CapabilitiesInstance;

    /// <inheritdoc />
    /// <exception cref="FontFormatException">If the font, or its outline table, is malformed.</exception>
    public void LoadFont(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_face is not null)
            throw new InvalidOperationException("Font already loaded. Create a new NativeRasterizer instance.");

        var face = NativeFontFace.Load(fontData, faceIndex);
        var variations = FontVariations.Load(face);
        var decoder = OutlineDecoders.ForFace(face, variations);

        _face = face;
        _variations = variations;
        _pipeline = new GlyphPipeline(decoder, face.Head.UnitsPerEm);
    }

    /// <summary>
    /// Selects the variable-font instance to render. Axes missing from
    /// <paramref name="userAxes"/> stay at their default and values are clamped to each axis's
    /// range. Ignored for a font without an <c>fvar</c> table.
    /// </summary>
    /// <param name="fvarAxes">The font's <c>fvar</c> axes, in order.</param>
    /// <param name="userAxes">User-scale values by axis tag (e.g. "wght" = 700).</param>
    /// <exception cref="RasterizationException">If the font's variation tables are malformed.</exception>
    public void SetVariationAxes(IReadOnlyList<VariationAxis> fvarAxes, Dictionary<string, float> userAxes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(fvarAxes);
        ArgumentNullException.ThrowIfNull(userAxes);

        try
        {
            _variations?.SetAxes(fvarAxes, userAxes);
        }
        catch (FontFormatException ex)
        {
            throw new RasterizationException($"The font's variation data is malformed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Not supported. The native rasterizer cannot load system fonts by name.
    /// </summary>
    public void LoadSystemFont(string familyName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        throw new NotSupportedException(
            "Native rasterizer does not support loading system fonts by name. Use LoadFont with font bytes instead.");
    }

    /// <inheritdoc />
    public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        var renderer = GlyphRenderers.Select(options);

        int glyphIndex = _face!.GetGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        var glyph = Prepare(codepoint, glyphIndex, options);
        if (glyph.IsBlank)
            return BlankGlyph(codepoint, glyphIndex, glyph.Advance);

        return renderer.Render(codepoint, glyphIndex, glyph, options);
    }

    /// <inheritdoc />
    public IReadOnlyList<RasterizedGlyph> RasterizeAll(IEnumerable<int> codepoints, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(codepoints);

        var results = new List<RasterizedGlyph>();
        foreach (int codepoint in codepoints)
        {
            var glyph = RasterizeGlyph(codepoint, options);
            if (glyph is not null)
                results.Add(glyph);
        }

        return results;
    }

    /// <inheritdoc />
    public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        var renderer = GlyphRenderers.Select(options);

        int glyphIndex = _face!.GetGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        var glyph = Prepare(codepoint, glyphIndex, options);
        if (glyph.IsBlank)
            return new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: glyph.Advance, Width: 0, Height: 0);

        return renderer.Measure(glyph, options);
    }

    /// <inheritdoc />
    public RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);

        double scale = GlyphPipeline.PixelScale(options, _face!.Head.UnitsPerEm);

        // OS/2 usWinAscent/usWinDescent, not hhea: these are what GDI reports as
        // tmAscent/tmDescent and therefore what bmfont.exe writes as base/lineHeight. Using
        // them reproduces, to the pixel, the shared calculation the pipeline falls back to for
        // FreeType, so swapping backends does not shift the line box. hhea is the fallback for
        // the rare face with no usable OS/2 metrics.
        //
        // Ceiling, not rounding: a line box a fraction of a pixel short clips the glyphs it is
        // supposed to contain.
        //
        // A variable font's MVAR moves usWinAscent/usWinDescent ('hcla'/'hcld') with the
        // instance; at the default instance both deltas are exactly zero.
        var os2 = _face.Os2;
        if (os2.WinAscent > 0)
        {
            double winAscent = os2.WinAscent + (double)MetricDelta("hcla");
            double winDescent = os2.WinDescent + (double)MetricDelta("hcld");
            return new RasterizerFontMetrics
            {
                Ascent = (int)Math.Ceiling(winAscent * scale),
                Descent = (int)Math.Ceiling(winDescent * scale),
                LineHeight = (int)Math.Ceiling((winAscent + winDescent) * scale)
            };
        }

        var hhea = _face.Hhea;
        return new RasterizerFontMetrics
        {
            Ascent = (int)Math.Ceiling(hhea.Ascender * scale),
            Descent = (int)Math.Ceiling(Math.Abs((double)hhea.Descender) * scale),
            LineHeight = (int)Math.Ceiling((hhea.Ascender - hhea.Descender + hhea.LineGap) * scale)
        };
    }

    /// <summary>
    /// Returns null to let the shared GPOS/kern parser handle kerning, the same as the
    /// StbTrueType backend.
    /// </summary>
    public IReadOnlyList<ScaledKerningPair>? GetKerningPairs(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _face = null;
        _pipeline = null;
        _variations = null;
    }

    private void EnsureFontLoaded()
    {
        if (_face is null)
            throw new InvalidOperationException("Font not loaded. Call LoadFont first.");
    }

    /// <summary>The glyph index a codepoint maps to, or 0 when unmapped.</summary>
    internal int GetGlyphIndex(int codepoint)
    {
        EnsureFontLoaded();
        return _face!.GetGlyphIndex(codepoint);
    }

    /// <summary>The <c>MVAR</c> delta for a font-wide metric at the current instance; 0 when unvaried.</summary>
    private float MetricDelta(string tag) => _variations?.GetMetricDelta(tag) ?? 0f;

    /// <summary>The parsed font face. Internal access for tests and later-phase decoders.</summary>
    internal NativeFontFace? Face => _face;

    /// <summary>
    /// Runs pipeline stages 1–4 for a glyph, reporting malformed glyph data against the
    /// codepoint that asked for it.
    /// </summary>
    private PreparedGlyph Prepare(int codepoint, int glyphIndex, RasterOptions options)
    {
        try
        {
            return _pipeline!.Prepare(glyphIndex, options, GlyphTransforms.For(options));
        }
        catch (FontFormatException ex)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
    }

    /// <summary>A whitespace glyph: a real advance, but nothing to draw.</summary>
    private static RasterizedGlyph BlankGlyph(int codepoint, int glyphIndex, int advance) =>
        new()
        {
            Codepoint = codepoint,
            GlyphIndex = glyphIndex,
            BitmapData = [],
            Width = 0,
            Height = 0,
            Pitch = 0,
            Metrics = new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: advance, Width: 0, Height: 0),
            Format = PixelFormat.Grayscale8
        };
}
