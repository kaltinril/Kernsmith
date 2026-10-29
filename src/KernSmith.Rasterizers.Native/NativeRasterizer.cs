using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Bitmaps;
using KernSmith.Rasterizers.Native.Internal.Color;
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
/// metrics. Color fonts render from <c>COLR</c> version 1 paint graphs and version 0 layers with
/// <c>CPAL</c> palettes when <see cref="RasterOptions.ColorFont"/> is on (Phase 172), and from
/// <c>CBDT</c>/<c>sbix</c> PNG bitmaps, scaled from the best strike, for glyphs without
/// <c>COLR</c> data; glyphs with none of these render as plain coverage. A font drawn only by
/// bitmaps renders them as grayscale ink coverage when color is off.
///
/// <see cref="RasterOptions.Bold"/> and <see cref="RasterOptions.Italic"/> are applied as
/// font-unit outline transforms matching the FreeType backend's synthetic styles (Phase 167):
/// FreeType's embolden strength, <c>max(0.5px, ppem/24)</c>, with the advance grown to match,
/// then a 12° shear — to every color layer and <c>COLR</c> version 1 glyph clip as well as plain outlines.
///
/// Distance fields are computed from the (transformed) outline: single-channel SDF (Phase 169)
/// and multi-channel MSDF/MTSDF (Phase 170), selected by <see cref="RasterOptions.EffectiveSdfMode"/>.
/// Outline stroking (Phase 168) is not implemented; its <see cref="IRasterizerCapabilities"/>
/// flag is false. <see cref="RasterOptions.SuperSample"/> is ignored: with
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
    private ColorGlyphs? _colorGlyphs;
    private BitmapGlyphs? _bitmapGlyphs;
    private FontVariations? _variations;
    private bool _disposed;

    /// <inheritdoc />
    public IRasterizerCapabilities Capabilities => CapabilitiesInstance;

    /// <inheritdoc />
    /// <exception cref="FontFormatException">If the font, or its outline table, is malformed (for a
    /// font with no outlines, its color bitmap tables).</exception>
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
        _pipeline = new GlyphPipeline(decoder, face.Head.UnitsPerEm, new GlyphHinter(face.GetGlyphIndex, face.Os2, () => InstanceStamp(variations)));
        _colorGlyphs = ColorGlyphs.TryLoad(face.Tables, face.Maxp.NumGlyphs, variations);
        _bitmapGlyphs = BitmapGlyphs.TryLoad(face);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// If the font has no usable <c>CPAL</c> table or no palette at <paramref name="paletteIndex"/>.
    /// </exception>
    public void SelectColorPalette(int paletteIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();

        if (_colorGlyphs is null)
            throw new ArgumentOutOfRangeException(nameof(paletteIndex), paletteIndex,
                "The font has no usable CPAL color palette table.");

        _colorGlyphs.SelectPalette(paletteIndex);
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
    /// <exception cref="ArgumentOutOfRangeException">If the size is not a positive finite number or the DPI is not positive.</exception>
    /// <exception cref="RasterizationException">
    /// If the glyph's data is malformed, or its bitmap would exceed the size limits (64 ems, or
    /// 16384 px per side after distance-field padding).
    /// </exception>
    public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        ValidateSize(options);
        var renderer = GlyphRenderers.SelectForFace(options, _colorGlyphs, _pipeline!, _bitmapGlyphs);

        int glyphIndex = _face!.GetGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        var glyph = Prepare(codepoint, glyphIndex, options);
        if (glyph.IsBlank && !DrawsLayers(renderer, glyphIndex))
            return BlankGlyph(codepoint, glyphIndex, glyph.Advance);

        try
        {
            return renderer.Render(codepoint, glyphIndex, glyph, options);
        }
        catch (RasterizationException ex) when (ex.Codepoint is null)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
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
    /// <exception cref="ArgumentOutOfRangeException">If the size is not a positive finite number or the DPI is not positive.</exception>
    /// <exception cref="RasterizationException">As for <see cref="RasterizeGlyph"/>.</exception>
    public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        ValidateSize(options);
        var renderer = GlyphRenderers.SelectForFace(options, _colorGlyphs, _pipeline!, _bitmapGlyphs);

        int glyphIndex = _face!.GetGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        // Where the box alone decides the metrics, skip decoding the outline (Phase 177).
        var transforms = GlyphTransforms.For(options, _face.Head.UnitsPerEm);
        if (renderer is IBoxMeasuringRenderer boxRenderer && boxRenderer.MeasuresFromBox(options) && TryPrepareBox(codepoint, glyphIndex, options, transforms, out var box, out int advance))
        {
            if (box.IsEmpty)
                return new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: advance, Width: 0, Height: 0);

            try
            {
                return boxRenderer.MeasureBox(glyphIndex, box, advance, options);
            }
            catch (RasterizationException ex) when (ex.Codepoint is null)
            {
                throw new RasterizationException(codepoint, ex.Message);
            }
        }

        var glyph = Prepare(codepoint, glyphIndex, options, transforms);
        if (glyph.IsBlank && !DrawsLayers(renderer, glyphIndex))
            return new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: glyph.Advance, Width: 0, Height: 0);

        try
        {
            return renderer.Measure(glyph, options);
        }
        catch (RasterizationException ex) when (ex.Codepoint is null)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">If the size is not a positive finite number or the DPI is not positive.</exception>
    public RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        ValidateSize(options);

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
        _colorGlyphs = null;
        _bitmapGlyphs = null;
        _variations = null;
    }

    /// <summary>
    /// Rejects a size or DPI no glyph can be scaled by: a zero, negative, NaN or infinite scale
    /// would yield blank glyphs with negative or saturated advances and line metrics.
    /// </summary>
    private static void ValidateSize(RasterOptions options)
    {
        if (!(options.Size > 0f) || float.IsPositiveInfinity(options.Size))
            throw new ArgumentOutOfRangeException(nameof(options), options.Size, "Size must be a positive, finite number of pixels.");
        if (options.Dpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Dpi, "Dpi must be positive.");
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

    /// <summary>Identifies the selected variation instance for the hinter's per-instance measurements.</summary>
    private static int InstanceStamp(FontVariations? variations)
    {
        if (variations is null || variations.IsDefaultInstance)
            return 0;

        var hash = new HashCode();
        foreach (float coordinate in variations.Coordinates)
            hash.Add(coordinate);
        return hash.ToHashCode();
    }

    /// <summary>The <c>MVAR</c> delta for a font-wide metric at the current instance; 0 when unvaried.</summary>
    private float MetricDelta(string tag) => _variations?.GetMetricDelta(tag) ?? 0f;

    /// <summary>The parsed font face. Internal access for tests and later-phase decoders.</summary>
    internal NativeFontFace? Face => _face;

    /// <summary>
    /// Runs pipeline stages 1–4 for a glyph, reporting malformed glyph data against the
    /// codepoint that asked for it.
    /// </summary>
    private PreparedGlyph Prepare(int codepoint, int glyphIndex, RasterOptions options) =>
        Prepare(codepoint, glyphIndex, options, GlyphTransforms.For(options, _face!.Head.UnitsPerEm));

    /// <inheritdoc cref="Prepare(int, int, RasterOptions)"/>
    private PreparedGlyph Prepare(int codepoint, int glyphIndex, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms)
    {
        try
        {
            return _pipeline!.Prepare(glyphIndex, options, transforms);
        }
        catch (FontFormatException ex)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
        catch (RasterizationException ex) when (ex.Codepoint is null)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
    }

    /// <summary>
    /// <see cref="GlyphPipeline.TryPrepareBox"/>, reporting malformed glyph data against the
    /// codepoint as <see cref="Prepare(int, int, RasterOptions)"/> does.
    /// </summary>
    private bool TryPrepareBox(int codepoint, int glyphIndex, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms, out Internal.Raster.GlyphBox box, out int advance)
    {
        try
        {
            return _pipeline!.TryPrepareBox(glyphIndex, options, transforms, out box, out advance);
        }
        catch (FontFormatException ex)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
        catch (RasterizationException ex) when (ex.Codepoint is null)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }
    }

    /// <summary>
    /// True when the glyph is drawn from color layers or a bitmap, so a blank base outline (common
    /// in <c>COLR</c> fonts, and every glyph of a bitmap-only font) must still reach the renderer.
    /// </summary>
    private static bool DrawsLayers(IGlyphRenderer renderer, int glyphIndex) =>
        renderer is IDrawsWithoutOutline drawer && drawer.DrawsWithoutOutline(glyphIndex);

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
