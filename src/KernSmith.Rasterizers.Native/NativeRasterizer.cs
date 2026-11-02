using KernSmith.Font.Models;
using KernSmith.Font.Tables;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Bitmaps;
using KernSmith.Rasterizers.Native.Internal.Color;
using KernSmith.Rasterizers.Native.Internal.Hinting;
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
/// <see cref="RasterOptions.WidthPercent"/> (condensed/expanded) and
/// <see cref="RasterOptions.ItalicAngle"/> are outline transforms too, and
/// <see cref="RasterizeOutlineLayer"/> strokes the vector outline for <see cref="OutlineMode.Vector"/>
/// (Phase 168). <see cref="RasterOptions.SuperSample"/> is ignored: with
/// <see cref="IRasterizerCapabilities.HandlesOwnSizing"/> false the pipeline already renders
/// at the supersampled size and box-filters the result back down, exactly as it does for the
/// FreeType backend — except for <see cref="AntiAliasMode.Lcd"/>, which it renders at the final
/// size. <see cref="RasterOptions.LcdFilter"/> and <see cref="RasterOptions.LcdSubpixelOrder"/>
/// select the LCD filter and panel layout (Phase 173).
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

    /// <summary>
    /// Per codepoint, how the last <see cref="RasterizeGlyph"/> under the kerning bake
    /// (<see cref="GlyphHinter.BakesKerning"/>) left the glyph's sides — whole columns of white
    /// in its rendered box and the coverage down each facing column — and the size that was at;
    /// what <see cref="GetKerningAdjustments"/> pairs up.
    /// </summary>
    private readonly Dictionary<int, GlyphSides> _sides = [];

    /// <summary>True for a fixed-pitch face (<see cref="GlyphHinter.IsFixedPitch"/>): no advance is rebuilt and no pair is kerned by the bake.</summary>
    private bool _fixedPitch;

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
        _fixedPitch = GlyphHinter.IsFixedPitch(face.Post.IsFixedPitch, PrintableAsciiAdvances(face));
        _pipeline = new GlyphPipeline(decoder, face.Head.UnitsPerEm, new GlyphHinter(face.GetGlyphIndex, face.Os2, () => InstanceStamp(variations), _fixedPitch, HintingProfile.For(face)));
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
    /// <exception cref="ArgumentOutOfRangeException">If the size is not a positive finite number or the DPI is negative (zero means 72).</exception>
    /// <exception cref="RasterizationException">
    /// If the glyph's data is malformed, or its bitmap would exceed the size limits (64 ems, or
    /// 16384 px per side after distance-field padding).
    /// </exception>
    public RasterizedGlyph? RasterizeGlyph(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        options = ValidateSize(options);
        var renderer = GlyphRenderers.SelectForFace(options, _colorGlyphs, _pipeline!, _bitmapGlyphs);

        int glyphIndex = _face!.GetGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        var glyph = Prepare(codepoint, glyphIndex, options);
        if (glyph.IsBlank && !DrawsLayers(renderer, glyphIndex))
        {
            _sides.Remove(codepoint);
            return WithFractional(BlankGlyph(codepoint, glyphIndex, glyph.Advance), glyph, options);
        }

        RasterizedGlyph rendered;
        try
        {
            rendered = renderer.Render(codepoint, glyphIndex, glyph, options);
        }
        catch (RasterizationException ex) when (ex.Codepoint is null)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }

        RecordSides(codepoint, glyph, rendered, options);
        return WithFractional(rendered, glyph, options);
    }

    /// <summary>
    /// The glyph with its unrounded metrics attached when <see cref="RasterOptions.FractionalMetrics"/>
    /// asks for them: the font's advance and left side bearing (the outline's control-box left,
    /// which is the <c>hmtx</c> bearing the decoder placed it by) at the size, after the request's
    /// transforms; under hinting, how far the fitted ink's left edge moved (tight bounds of the
    /// fitted outline against the unhinted one — tight, because the hint stage adds extremum
    /// nodes that tighten the control hull without moving any ink) and whatever the hint stage
    /// added to the advance over the font's rounded once: the stem-pair padding column, a blank
    /// glyph's word-gap rounding, an integer-ppem face's rounding at its own ppem. Otherwise
    /// the glyph as rendered.
    /// </summary>
    private RasterizedGlyph WithFractional(RasterizedGlyph rendered, PreparedGlyph glyph, RasterOptions options)
    {
        if (!options.FractionalMetrics)
            return rendered;

        float scale = GlyphPipeline.PixelScale(options, _face!.Head.UnitsPerEm);
        var unhinted = glyph.UnhintedOutline ?? glyph.Outline;
        float advance = glyph.AdvanceWidth * scale;
        float bearing = unhinted.XMin * scale;

        float? advanceDelta = null, bearingDelta = null;
        if (glyph.UnhintedOutline is not null && !ReferenceEquals(glyph.UnhintedOutline, glyph.Outline) && !glyph.Outline.IsEmpty)
        {
            float before = OutlineBounds.Tight(unhinted).XMin * scale;
            float after = glyph.ToPixels.ToPixelX(OutlineBounds.Tight(glyph.Outline).XMin);
            bearingDelta = after - before;
        }

        // An unhinted glyph's advance is PixelAdvance by construction, so this is 0 exactly there.
        int padding = glyph.Advance - GlyphPipeline.PixelAdvance(glyph.AdvanceWidth, scale);
        if (padding != 0)
            advanceDelta = padding;

        return new RasterizedGlyph
        {
            Codepoint = rendered.Codepoint,
            GlyphIndex = rendered.GlyphIndex,
            BitmapData = rendered.BitmapData,
            Width = rendered.Width,
            Height = rendered.Height,
            Pitch = rendered.Pitch,
            Metrics = rendered.Metrics,
            Format = rendered.Format,
            Fractional = new FractionalGlyphMetrics(advance, bearing, advanceDelta, bearingDelta),
        };
    }

    /// <summary>Remembers (or forgets) a glyph's sides for <see cref="GetKerningAdjustments"/>.</summary>
    private void RecordSides(int codepoint, PreparedGlyph glyph, RasterizedGlyph rendered, RasterOptions options)
    {
        if (!GlyphHinter.BakesKerning(options) || glyph.Spacing is null || rendered.Width == 0 || rendered.Format != PixelFormat.Grayscale8)
        {
            _sides.Remove(codepoint);
            return;
        }

        var left = new byte[rendered.Height];
        var right = new byte[rendered.Height];
        for (int y = 0; y < rendered.Height; y++)
        {
            left[y] = rendered.BitmapData[y * rendered.Pitch];
            right[y] = rendered.BitmapData[(y * rendered.Pitch) + rendered.Width - 1];
        }

        _sides[codepoint] = new GlyphSides(
            rendered.Metrics.BearingX, rendered.Metrics.Advance - rendered.Metrics.BearingX - rendered.Width, rendered.Metrics.BearingY, left, right, options);
    }

    /// <summary>The font-unit advances of the printable ASCII glyphs a face maps, for the fixed-pitch test.</summary>
    private static IEnumerable<int> PrintableAsciiAdvances(NativeFontFace face)
    {
        for (int codepoint = 0x20; codepoint < 0x7F; codepoint++)
        {
            int glyphIndex = face.GetGlyphIndex(codepoint);
            if (glyphIndex != 0 && glyphIndex < face.Hmtx.GlyphCount)
                yield return face.Hmtx.GetAdvanceWidth(glyphIndex);
        }
    }

    /// <summary>
    /// The glyph dilated by <paramref name="width"/> pixels — every point within that distance of
    /// its filled region, measured with a round, square (Chebyshev) or diamond (Manhattan, for
    /// <see cref="OutlineShape.Plus"/>) pen — in the same frame and anti-alias mode as
    /// <see cref="RasterizeGlyph"/> (LCD as grayscale coverage, since the layer is one color).
    /// </summary>
    /// <returns>Null for an unmapped or blank glyph, for distance fields, and when
    /// <see cref="RasterOptions.ColorFont"/> is on for a face with color glyphs (<c>COLR</c> layers
    /// or <c>CBDT</c>/<c>sbix</c> bitmaps): a color glyph's silhouette is its layers' or image's,
    /// so the pipeline's bitmap outline draws every outline of such a font, keeping them
    /// consistent. A bitmap-only face has no outlines, so it never gets a layer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If the size, DPI, width or height scale is out of range.</exception>
    /// <exception cref="RasterizationException">As for <see cref="RasterizeGlyph"/>.</exception>
    public RasterizedGlyph? RasterizeOutlineLayer(int codepoint, RasterOptions options, int width, OutlineShape shape, float heightScale = 1f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        options = ValidateSize(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        if (!(heightScale > 0) || !float.IsFinite(heightScale))
            throw new ArgumentOutOfRangeException(nameof(heightScale), heightScale, "Height scale must be positive and finite.");

        if (options.EffectiveSdfMode != SdfMode.None || (options.ColorFont && (_colorGlyphs is not null || _bitmapGlyphs is not null)))
            return null;

        int glyphIndex = _face!.GetGlyphIndex(codepoint);
        if (glyphIndex == 0)
            return null;

        var glyph = Prepare(codepoint, glyphIndex, options);
        if (glyph.IsBlank)
            return null;

        try
        {
            return OutlineLayer.Render(codepoint, glyphIndex, glyph, width, shape, heightScale, aliased: options.AntiAlias == AntiAliasMode.None);
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
    /// <exception cref="ArgumentOutOfRangeException">If the size is not a positive finite number or the DPI is negative (zero means 72).</exception>
    /// <exception cref="RasterizationException">As for <see cref="RasterizeGlyph"/>.</exception>
    public GlyphMetrics? GetGlyphMetrics(int codepoint, RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        options = ValidateSize(options);
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

        // COLR version 0 layers likewise measure from the layers' boxes where each has one.
        if (renderer is ColorLayerRenderer layered && TryMeasureLayers(codepoint, glyphIndex, layered, options, transforms, out var layeredMetrics))
            return layeredMetrics;

        var glyph = Prepare(codepoint, glyphIndex, options, transforms);
        if (glyph.IsBlank && !DrawsLayers(renderer, glyphIndex))
            return new GlyphMetrics(BearingX: 0, BearingY: 0, Advance: glyph.Advance, Width: 0, Height: 0);

        GlyphMetrics measured;
        try
        {
            measured = renderer.Measure(glyph, options);
        }
        catch (RasterizationException ex) when (ex.Codepoint is null)
        {
            throw new RasterizationException(codepoint, ex.Message);
        }

        return measured;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">If the size is not a positive finite number or the DPI is negative (zero means 72).</exception>
    public RasterizerFontMetrics? GetFontMetrics(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureFontLoaded();
        ArgumentNullException.ThrowIfNull(options);
        options = ValidateSize(options);

        // In double, like the shared calculation, rather than GlyphPipeline.PixelScale's float:
        // the line box should carry only the noise already in the float Size.
        double scale = (double)options.Size * options.Dpi / 72.0 / _face!.Head.UnitsPerEm;

        // OS/2 usWinAscent/usWinDescent, not hhea: these are what GDI reports as
        // tmAscent/tmDescent and therefore what bmfont.exe writes as base/lineHeight. Using
        // them reproduces, to the pixel, the shared calculation the pipeline falls back to for
        // FreeType, so swapping backends does not shift the line box. hhea is the fallback for
        // the rare face with no usable OS/2 metrics.
        //
        // Ceiling, not rounding: a line box a fraction of a pixel short clips the glyphs it is
        // supposed to contain. CeilingToPixels, not Math.Ceiling: with BMFont cell sizing the
        // Size is a float ppem chosen so the win box is exactly the requested size, and a plain
        // ceiling turns its float noise into size + 1 (issue #266).
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
                Ascent = RasterizerFontMetrics.CeilingToPixels(winAscent * scale),
                Descent = RasterizerFontMetrics.CeilingToPixels(winDescent * scale),
                LineHeight = RasterizerFontMetrics.CeilingToPixels((winAscent + winDescent) * scale)
            };
        }

        var hhea = _face.Hhea;
        return new RasterizerFontMetrics
        {
            Ascent = RasterizerFontMetrics.CeilingToPixels(hhea.Ascender * scale),
            Descent = RasterizerFontMetrics.CeilingToPixels(Math.Abs((double)hhea.Descender) * scale),
            LineHeight = RasterizerFontMetrics.CeilingToPixels((hhea.Ascender - hhea.Descender + hhea.LineGap) * scale)
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

    /// <summary>A rendered glyph's sides, as the kerning bake sees them.</summary>
    /// <param name="LeftColumns">Whole columns of white before the ink (the box's x offset).</param>
    /// <param name="RightColumns">Whole columns of white after the ink to the advance; negative when the ink overhangs it.</param>
    /// <param name="Top">The box's top row above the baseline (the y bearing), to line two glyphs' rows up.</param>
    /// <param name="LeftColumn">The coverage down the ink's first column, top row first.</param>
    /// <param name="RightColumn">The coverage down the ink's last column, top row first.</param>
    /// <param name="Options">The request the glyph was rendered for; only sides from the same request are paired.</param>
    private readonly record struct GlyphSides(int LeftColumns, int RightColumns, int Top, byte[] LeftColumn, byte[] RightColumn, RasterOptions Options)
    {
        /// <summary>
        /// True when this glyph's last column and <paramref name="right"/>'s first column, set
        /// side by side, are both solid (<see cref="StemCoverage"/> or more) on some shared row:
        /// two stems, or a stem and a bar's end, that meet with no white between them and read
        /// as one stroke. A bowl's fringe against a stem stays under the threshold.
        /// </summary>
        public bool FusesWith(GlyphSides right)
        {
            int top = Math.Min(Top, right.Top);
            int bottom = Math.Max(Top - RightColumn.Length, right.Top - right.LeftColumn.Length);
            for (int y = top; y > bottom; y--)
            {
                if (RightColumn[Top - y] >= StemCoverage && right.LeftColumn[right.Top - y] >= StemCoverage)
                    return true;
            }

            return false;
        }
    }

    /// <summary>The coverage (of 255) at which a facing column counts as solid ink: three quarters.</summary>
    private const byte StemCoverage = 191;

    /// <summary>A pair with this many columns of white or more can give one up to balance a pair that was opened.</summary>
    private const int SlackColumns = 2;

    /// <summary>
    /// The kerning bake: the per-pair corrections over every ordered pair of glyphs this
    /// instance has rasterized for this request under <see cref="GlyphHinter.BakesKerning"/>. The
    /// <c>Normal</c> fit keeps every advance the font's and places each glyph by side, so a few
    /// pairs come out with no white between their inks; those are the only pairs opened. In
    /// bilevel any pair with no column between the inks is opened to one column; in grayscale
    /// only a pair whose facing columns are both solid (three-quarter coverage) on a shared
    /// row — two stems, which fuse into one stroke ("rn" into "m") — since a bowl's fringe or a
    /// diagonal against a stem still reads as two letters. The
    /// bake leans zero-sum: each pixel a left glyph's pairs gained is taken back from one of
    /// that glyph's pairs with at least <see cref="SlackColumns"/> columns of white, widest
    /// first, while such pairs last, so a line's length stays the font's. Pairs of two digits
    /// (tabular columns) and pairs with a glyph that has no ink are never touched. Only non-zero
    /// corrections are reported; null when nothing was recorded, when the face is fixed pitch
    /// (every advance is the pitch and no pair may move), or when the bake is off for the request.
    /// </summary>
    public IReadOnlyList<ScaledKerningAdjustment>? GetKerningAdjustments(RasterOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        options = ValidateSize(options);
        if (_fixedPitch || !GlyphHinter.BakesKerning(options))
            return null;

        // Only glyphs rendered for this very request pair up: the sides of an earlier size or
        // anti-alias mode on the same instance are not this atlas's.
        var codepoints = new List<int>();
        foreach (var (codepoint, sides) in _sides)
        {
            if (sides.Options == options)
                codepoints.Add(codepoint);
        }

        if (codepoints.Count == 0)
            return null;

        codepoints.Sort();
        bool bilevel = options.AntiAlias == AntiAliasMode.None;
        var adjustments = new List<ScaledKerningAdjustment>();
        var slack = new List<(int Right, int Gap)>();
        foreach (int left in codepoints)
        {
            var leftSides = _sides[left];
            int opened = 0;
            slack.Clear();
            foreach (int right in codepoints)
            {
                if (IsAsciiDigit(left) && IsAsciiDigit(right))
                    continue;

                var rightSides = _sides[right];
                int gap = leftSides.RightColumns + rightSides.LeftColumns;
                bool fuses = gap <= 0 && (bilevel || leftSides.FusesWith(rightSides));
                if (fuses)
                {
                    adjustments.Add(new ScaledKerningAdjustment(left, right, 1 - gap));
                    opened += 1 - gap;
                }
                else if (gap >= SlackColumns)
                {
                    slack.Add((right, gap));
                }
            }

            // Widest pairs first, then by codepoint, so the choice is the same every run.
            slack.Sort((a, b) => a.Gap != b.Gap ? b.Gap.CompareTo(a.Gap) : a.Right.CompareTo(b.Right));
            for (int i = 0; i < slack.Count && opened > 0; i++, opened--)
                adjustments.Add(new ScaledKerningAdjustment(left, slack[i].Right, -1f));
        }

        return adjustments;

        static bool IsAsciiDigit(int codepoint) => codepoint is >= '0' and <= '9';
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sides.Clear();
        _face = null;
        _pipeline = null;
        _colorGlyphs = null;
        _bitmapGlyphs = null;
        _variations = null;
    }

    /// <summary>
    /// Rejects a size or DPI no glyph can be scaled by: a zero, negative, NaN or infinite scale
    /// would yield blank glyphs with negative or saturated advances and line metrics. A DPI of
    /// zero means 72, as it does to FreeType, so the request comes back with that filled in.
    /// </summary>
    private static RasterOptions ValidateSize(RasterOptions options)
    {
        if (!(options.Size > 0f) || float.IsPositiveInfinity(options.Size))
            throw new ArgumentOutOfRangeException(nameof(options), options.Size, "Size must be a positive, finite number of pixels.");
        if (options.Dpi < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.Dpi, "Dpi must not be negative.");

        return options.Dpi == 0 ? options with { Dpi = 72 } : options;
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

    /// <inheritdoc cref="ColorLayerRenderer.TryMeasureFromBoxes"/>
    private static bool TryMeasureLayers(int codepoint, int glyphIndex, ColorLayerRenderer renderer, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms, out GlyphMetrics metrics)
    {
        try
        {
            return renderer.TryMeasureFromBoxes(glyphIndex, options, transforms, out metrics);
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
