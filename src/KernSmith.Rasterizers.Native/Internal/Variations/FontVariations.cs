using KernSmith.Font.Tables;
using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal.Variations;

/// <summary>
/// The variation state of one loaded face: its variation tables and the normalized coordinates
/// of the instance currently selected. Outline deltas (<c>gvar</c>), advance deltas
/// (<c>HVAR</c>) and font-metric deltas (<c>MVAR</c>) are all answered for that instance.
/// </summary>
/// <remarks>
/// Created at load for every face with an <c>fvar</c> table, but the variation tables are only
/// parsed when an instance is first selected, so a damaged optional table cannot stop a font from
/// loading at its default instance. Until then — and whenever every coordinate is zero —
/// <see cref="IsDefaultInstance"/> is true and callers must take their unvaried paths, which is
/// what keeps the default instance byte-identical. Not thread-safe, like the rasterizer.
/// </remarks>
internal sealed class FontVariations : IGlyphVariationProvider
{
    private readonly NativeFontFace _face;
    private bool _tablesLoaded;
    private AvarTable? _avar;
    private GvarTable? _gvar;
    private HvarTable? _hvar;
    private MvarTable? _mvar;

    private float[] _coordinates;
    private float[] _hvarScalars = [];
    private float[] _mvarScalars = [];

    private FontVariations(NativeFontFace face, int axisCount)
    {
        _face = face;
        AxisCount = axisCount;
        _coordinates = new float[axisCount];
    }

    /// <summary>The <c>fvar</c> axis count.</summary>
    public int AxisCount { get; }

    /// <summary>True while no axis is away from its default — nothing varies.</summary>
    public bool IsDefaultInstance { get; private set; } = true;

    /// <summary>The normalized coordinates of the current instance, in <c>fvar</c> axis order.</summary>
    public ReadOnlySpan<float> Coordinates => _coordinates;

    /// <summary>True when advances come from <c>HVAR</c> rather than <c>gvar</c> phantom points.</summary>
    public bool HasAdvanceVariations => _hvar is not null;

    /// <summary>The variation state for a face, or null when it is not a variable font.</summary>
    public static FontVariations? Load(NativeFontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);

        // fvar: majorVersion, minorVersion, axesArrayOffset, reserved, then axisCount.
        if (face.Tables.TryGetTable("fvar") is not { Length: >= 10 } fvar)
            return null;

        int axisCount = (fvar.Span[8] << 8) | fvar.Span[9];
        return axisCount == 0 ? null : new FontVariations(face, axisCount);
    }

    /// <summary>
    /// Selects the instance at the given user-scale axis values.
    /// </summary>
    /// <param name="axes">The font's <c>fvar</c> axes, in order (as core parses them).</param>
    /// <param name="userValues">Requested values by tag; others stay at their default.</param>
    /// <exception cref="FontFormatException">If a variation table is malformed.</exception>
    public void SetAxes(IReadOnlyList<VariationAxis> axes, IReadOnlyDictionary<string, float> userValues)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(userValues);
        EnsureTablesLoaded();

        var normalized = AxisNormalizer.Normalize(axes, userValues, _avar);
        var coordinates = new float[AxisCount];
        normalized.AsSpan(0, Math.Min(normalized.Length, AxisCount)).CopyTo(coordinates);

        _coordinates = coordinates;
        IsDefaultInstance = Array.TrueForAll(coordinates, c => c.Equals(0f));
        _hvarScalars = _hvar?.Store.ComputeRegionScalars(coordinates) ?? [];
        _mvarScalars = _mvar?.Store?.ComputeRegionScalars(coordinates) ?? [];
    }

    /// <summary>The <c>HVAR</c> advance-width delta for a glyph at this instance, in font units.</summary>
    public float GetAdvanceDelta(int glyphIndex) =>
        IsDefaultInstance || _hvar is null ? 0f : _hvar.GetAdvanceDelta(glyphIndex, _hvarScalars);

    /// <summary>The <c>MVAR</c> delta for a value tag (e.g. 'hcla') at this instance, in font units.</summary>
    public float GetMetricDelta(string tag) =>
        IsDefaultInstance || _mvar is null ? 0f : _mvar.GetDelta(tag, _mvarScalars);

    /// <inheritdoc />
    public bool TryGetDeltas(
        int glyphIndex,
        int pointCount,
        ReadOnlySpan<int> xs,
        ReadOnlySpan<int> ys,
        ReadOnlySpan<int> contourEnds,
        out float[] dx,
        out float[] dy)
    {
        if (IsDefaultInstance || _gvar is null)
        {
            dx = dy = [];
            return false;
        }

        return _gvar.TryGetDeltas(glyphIndex, _coordinates, pointCount, xs, ys, contourEnds, out dx, out dy);
    }

    private void EnsureTablesLoaded()
    {
        if (_tablesLoaded)
            return;

        var tables = _face.Tables;
        _avar = tables.TryGetTable("avar") is { } avar ? AvarTable.Parse(avar.Span) : null;
        _gvar = tables.TryGetTable("gvar") is { } gvar ? GvarTable.Parse(gvar) : null;
        _hvar = tables.TryGetTable("HVAR") is { } hvar ? HvarTable.Parse(hvar.Span) : null;
        _mvar = tables.TryGetTable("MVAR") is { } mvar ? MvarTable.Parse(mvar.Span) : null;
        _tablesLoaded = true;
    }
}
