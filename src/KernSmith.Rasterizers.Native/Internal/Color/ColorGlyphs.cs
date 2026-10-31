using KernSmith.Rasterizers.Native.Internal.Tables;
using KernSmith.Rasterizers.Native.Internal.Variations;

namespace KernSmith.Rasterizers.Native.Internal.Color;

/// <summary>A resolved color layer: the outline glyph to draw and the color to fill it with.</summary>
internal readonly record struct ColorLayer(int GlyphIndex, PaletteColor Color);

/// <summary>
/// A face's <c>COLR</c> color glyphs — version 1 paint graphs and version 0 layers — resolved
/// against its <c>CPAL</c> palettes, plus the palette the caller selected.
/// </summary>
/// <remarks>
/// Built once per face by <see cref="TryLoad"/>. Malformed or unusable color tables are treated
/// as absent — the face still renders, as plain coverage — because a broken optional table
/// should not make an otherwise good font unusable.
///
/// Layer resolution follows FreeType, the reference backend: layers are read in order and the
/// list ends at the first one that names a glyph outside the font, a palette entry outside the
/// palette, or a record past the layer array. A glyph whose first layer is already invalid has
/// no color layers and renders as plain coverage.
///
/// Version 1 paint graphs take precedence over version 0 layers for a glyph that has both, as the
/// spec directs; <see cref="ColrPaintReader"/> documents how invalid paints and oversized graphs
/// are handled. A malformed version 1 header or list drops the version 1 data only.
///
/// The foreground entry (<see cref="ColrTable.ForegroundPaletteIndex"/>) is opaque white when
/// the selected palette is flagged <see cref="CpalTable.UsableWithDarkBackground"/> and opaque
/// black otherwise — the default FreeType documents for <c>FT_Palette_Set_Foreground_Color</c>.
/// </remarks>
internal sealed class ColorGlyphs
{
    private static readonly PaletteColor Black = new(0, 0, 0, 255);
    private static readonly PaletteColor White = new(255, 255, 255, 255);

    private readonly ColrTable? _colr;
    private readonly ColrV1Table? _colrV1;
    private readonly CpalTable _cpal;
    private readonly int _glyphCount;
    private readonly FontVariations? _variations;

    private ColorGlyphs(ColrTable? colr, ColrV1Table? colrV1, CpalTable cpal, int glyphCount, FontVariations? variations)
    {
        _colr = colr;
        _colrV1 = colrV1;
        _cpal = cpal;
        _glyphCount = glyphCount;
        _variations = variations;
    }

    /// <summary>The number of palettes the font offers.</summary>
    public int PaletteCount => _cpal.PaletteCount;

    /// <summary>The palette layers are colored from; 0 until <see cref="SelectPalette"/> is called.</summary>
    public int SelectedPalette { get; private set; }

    /// <summary>
    /// Reads a face's color tables. Returns null when it has no usable <c>CPAL</c> table; the
    /// result has no layers when <c>COLR</c> is absent or malformed.
    /// </summary>
    /// <param name="tables">The face's tables.</param>
    /// <param name="glyphCount">The face's <c>maxp.numGlyphs</c>; layers naming glyphs past it are invalid.</param>
    /// <param name="variations">The face's variation state, which version 1 paints vary with; null for a static font.</param>
    public static ColorGlyphs? TryLoad(TableProvider tables, int glyphCount, FontVariations? variations = null)
    {
        ArgumentNullException.ThrowIfNull(tables);

        CpalTable cpal;
        try
        {
            var cpalData = tables.TryGetTable("CPAL");
            if (cpalData is null)
                return null;
            cpal = CpalTable.Parse(cpalData.Value.Span);
        }
        catch (FontFormatException)
        {
            return null;
        }

        ColrTable? colr = null;
        ColrV1Table? colrV1 = null;
        var colrData = tables.TryGetTable("COLR");
        if (colrData is not null)
        {
            try
            {
                colr = ColrTable.Parse(colrData.Value.Span);
            }
            catch (FontFormatException)
            {
                colr = null;
            }

            try
            {
                colrV1 = ColrV1Table.Parse(colrData.Value);
            }
            catch (Exception ex) when (ex is FontFormatException or OverflowException)
            {
                colrV1 = null;
            }
        }

        return new ColorGlyphs(colr, colrV1, cpal, glyphCount, variations);
    }

    /// <summary>Selects the palette layers are colored from.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If the font has no such palette.</exception>
    public void SelectPalette(int paletteIndex)
    {
        if ((uint)paletteIndex >= (uint)_cpal.PaletteCount)
            throw new ArgumentOutOfRangeException(nameof(paletteIndex), paletteIndex,
                $"The font's CPAL table has {_cpal.PaletteCount} palette(s), indexed from 0.");
        SelectedPalette = paletteIndex;
    }

    /// <summary>
    /// Reads the glyph's version 1 paint graph at the selected palette and the current
    /// variable-font instance.
    /// </summary>
    /// <returns>False when the glyph has no version 1 definition, or its graph exceeds the reader's limits.</returns>
    public bool TryGetPaintGraph(int glyphIndex, out ColrGlyphGraph graph)
    {
        graph = null!;
        if (_colrV1 is null)
            return false;

        float[]? scalars = null;
        if (_colrV1.VariationStore is { } store && _variations is { IsDefaultInstance: false } variations)
            scalars = store.ComputeRegionScalars(variations.Coordinates);

        return new ColrPaintReader(_colrV1, _cpal, SelectedPalette, Foreground, _glyphCount, scalars)
            .TryRead(glyphIndex, out graph);
    }

    /// <summary>True when the glyph renders from a version 1 paint graph.</summary>
    public bool HasPaintGraph(int glyphIndex) => TryGetPaintGraph(glyphIndex, out _);

    /// <summary>
    /// True when the glyph has a version 1 base glyph record — a lookup, without reading the
    /// graph. A superset of <see cref="HasPaintGraph"/>: a graph past the reader's limits counts.
    /// </summary>
    public bool HasPaintGraphRecord(int glyphIndex) => _colrV1 is not null && _colrV1.TryGetBasePaint(glyphIndex, out _);

    /// <summary>True when the glyph has at least one valid version 0 color layer.</summary>
    public bool HasLayers(int glyphIndex) =>
        _colr is not null
        && _colr.TryGetLayerRange(glyphIndex, out int first, out int count)
        && count > 0
        && IsValid(first);

    /// <summary>
    /// Appends the glyph's valid color layers, bottom first, colored from the selected palette.
    /// Appends nothing when the glyph has none.
    /// </summary>
    public void GetLayers(int glyphIndex, List<ColorLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (_colr is null || !_colr.TryGetLayerRange(glyphIndex, out int first, out int count))
            return;

        for (int i = first; i < first + count && IsValid(i); i++)
        {
            var record = _colr.GetLayer(i);
            var color = record.PaletteIndex == ColrTable.ForegroundPaletteIndex
                ? Foreground
                : _cpal.GetColor(SelectedPalette, record.PaletteIndex);
            layers.Add(new ColorLayer(record.GlyphIndex, color));
        }
    }

    private PaletteColor Foreground =>
        (_cpal.GetPaletteType(SelectedPalette) & CpalTable.UsableWithDarkBackground) != 0 ? White : Black;

    private bool IsValid(int layerIndex)
    {
        if (layerIndex >= _colr!.LayerCount)
            return false;

        var record = _colr.GetLayer(layerIndex);
        return record.GlyphIndex < _glyphCount
            && (record.PaletteIndex < _cpal.EntryCount || record.PaletteIndex == ColrTable.ForegroundPaletteIndex);
    }
}
