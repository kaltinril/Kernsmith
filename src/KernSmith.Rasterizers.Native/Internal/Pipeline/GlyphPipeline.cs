using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native.Internal.Outlines;
using KernSmith.Rasterizers.Native.Internal.Raster;

namespace KernSmith.Rasterizers.Native.Internal.Pipeline;

/// <summary>
/// Runs stages 1–4 of the glyph pipeline — decode, transform, bounds, scale/hint — producing
/// the <see cref="PreparedGlyph"/> that rendering and metrics both consume. Stage 5, render-mode
/// dispatch, is <see cref="GlyphRenderers"/>.
/// </summary>
internal sealed class GlyphPipeline
{
    private readonly IOutlineDecoder _decoder;
    private readonly int _unitsPerEm;
    private readonly GlyphHinter? _hinter;

    /// <summary>Creates a pipeline over one face's decoder.</summary>
    /// <param name="decoder">Stage 1, chosen for the face's outline format.</param>
    /// <param name="unitsPerEm">The face's <c>head.unitsPerEm</c>.</param>
    /// <param name="hinter">The face's auto-hinter; null renders every request unhinted.</param>
    public GlyphPipeline(IOutlineDecoder decoder, int unitsPerEm, GlyphHinter? hinter = null)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitsPerEm);
        _decoder = decoder;
        _unitsPerEm = unitsPerEm;
        _hinter = hinter;
    }

    /// <summary>The face's <c>head.unitsPerEm</c>, for building <see cref="GlyphTransforms.For"/>.</summary>
    public int UnitsPerEm => _unitsPerEm;

    /// <summary>
    /// Pixels per font unit. <see cref="IRasterizerCapabilities.HandlesOwnSizing"/> is false,
    /// so <see cref="RasterOptions.Size"/> is already the em size the pipeline wants; only the
    /// point-to-pixel DPI conversion is applied on top of it.
    /// </summary>
    public static float PixelScale(RasterOptions options, int unitsPerEm) =>
        options.Size * options.Dpi / 72f / unitsPerEm;

    /// <summary>
    /// The font-unit to pixel mapping <see cref="Prepare"/> gives an unhinted glyph — for
    /// renderers that place font-unit geometry which is not a glyph outline (COLR paint
    /// coordinates, clip boxes).
    /// </summary>
    public OutlineTransform FontUnitsToPixels(RasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new OutlineTransform(PixelScale(options, _unitsPerEm), 0f);
    }

    /// <summary>Decodes, transforms, bounds and scales one glyph.</summary>
    /// <param name="glyphIndex">A glyph index inside the font.</param>
    /// <param name="options">The request.</param>
    /// <param name="transforms">Stage 2, applied in order; normally <see cref="GlyphTransforms.For"/>.</param>
    public PreparedGlyph Prepare(int glyphIndex, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transforms);

        float scale = PixelScale(options, _unitsPerEm);

        // 1. Decode, and 2. transform, in font units.
        var glyph = Shape(glyphIndex, options, transforms);

        // 3. Bounds, and 4. scale: the box lands on the pixel grid with the baseline on row 0,
        // its own Y origin placing the bitmap against it.
        var box = GlyphBox.Compute(glyph.Outline, glyph.DeclaredBounds, scale);
        int advance = (int)Math.Round(glyph.AdvanceWidth * (double)scale);
        var prepared = new PreparedGlyph(glyph.Outline, new OutlineTransform(scale, 0f), box, advance) { GlyphIndex = glyphIndex };

        // 4. Pixel-space hinting, measuring reference glyphs through the same stages 1-2.
        return _hinter is null || !GlyphHinter.Applies(options)
            ? prepared
            : Hint(prepared, options, transforms);
    }

    /// <summary>
    /// Stages 1–4 reduced to the box and advance, for measuring: the decoder's declared bounds
    /// placed on the grid exactly as <see cref="Prepare"/> places them, without decoding the
    /// outline. An empty box means a blank glyph.
    /// </summary>
    /// <returns>False when the box depends on the outline itself — a transform or the hint
    /// stage would run, the decoder cannot declare the bounds, or the declared box covers no
    /// pixel (<see cref="GlyphBox.Compute"/> then measures the outline) — and the caller must
    /// <see cref="Prepare"/> instead.</returns>
    public bool TryPrepareBox(int glyphIndex, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms, out GlyphBox box, out int advance)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transforms);

        box = default;
        advance = 0;
        if (transforms.Count != 0 || (_hinter is not null && GlyphHinter.Applies(options))
            || !_decoder.TryDecodeBounds(glyphIndex, options, out var bounds))
            return false;

        float scale = PixelScale(options, _unitsPerEm);
        advance = (int)Math.Round(bounds.AdvanceWidth * (double)scale);
        if (bounds.IsEmpty)
            return true;

        // The probe stands in for the undecoded outline: GlyphBox takes a non-empty outline's
        // grid from its declared box, and falls back to the outline only when that box covers
        // nothing — which the probe's zero hull turns into an empty box, sending the caller
        // down the full path.
        box = GlyphBox.Compute(DeclaredBoxProbe, bounds.DeclaredBounds, scale);
        return !box.IsEmpty;
    }

    /// <summary>A non-empty outline with an all-zero hull; see <see cref="TryPrepareBox"/>.</summary>
    private static readonly GlyphOutline DeclaredBoxProbe = new([OutlineCommand.Move(0f, 0f)], 0f, 0f, 0f, 0f);

    /// <summary>The hint stage, kept out of <see cref="Prepare"/> so the reference-glyph
    /// callback's closure is only allocated for a request that is actually hinted.</summary>
    private PreparedGlyph Hint(PreparedGlyph prepared, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms) =>
        _hinter!.Apply(prepared, options, _unitsPerEm, reference => ReferenceShape(reference, options, transforms));

    private DecodedGlyph Shape(int glyphIndex, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms)
    {
        var glyph = _decoder.Decode(glyphIndex, options);
        for (int i = 0; i < transforms.Count; i++)
            glyph = transforms[i].Apply(glyph);

        return glyph;
    }

    /// <summary>A reference glyph's outline for hint measurement; a malformed one counts as missing
    /// rather than failing the glyph actually being rendered.</summary>
    private GlyphOutline? ReferenceShape(int glyphIndex, RasterOptions options, IReadOnlyList<IGlyphTransform> transforms)
    {
        try
        {
            return Shape(glyphIndex, options, transforms).Outline;
        }
        catch (Exception ex) when (ex is FontFormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
