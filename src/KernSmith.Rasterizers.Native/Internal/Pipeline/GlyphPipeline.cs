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

    /// <summary>Creates a pipeline over one face's decoder.</summary>
    /// <param name="decoder">Stage 1, chosen for the face's outline format.</param>
    /// <param name="unitsPerEm">The face's <c>head.unitsPerEm</c>.</param>
    public GlyphPipeline(IOutlineDecoder decoder, int unitsPerEm)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitsPerEm);
        _decoder = decoder;
        _unitsPerEm = unitsPerEm;
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

        // 1. Decode.
        var glyph = _decoder.Decode(glyphIndex, options);

        // 2. Transform, in font units.
        for (int i = 0; i < transforms.Count; i++)
            glyph = transforms[i].Apply(glyph);

        // 3. Bounds, and 4. scale: the box lands on the pixel grid with the baseline on row 0,
        // its own Y origin placing the bitmap against it.
        var box = GlyphBox.Compute(glyph.Outline, glyph.DeclaredBounds, scale);
        int advance = (int)Math.Round(glyph.AdvanceWidth * (double)scale);
        var prepared = new PreparedGlyph(glyph.Outline, new OutlineTransform(scale, 0f), box, advance) { GlyphIndex = glyphIndex };

        // 4. Pixel-space hint hook.
        return GlyphHinter.Apply(prepared, options);
    }
}
