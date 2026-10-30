namespace KernSmith.Rasterizers.Native.Internal.Cff;

/// <summary>Everything a charstring can reach outside its own bytes.</summary>
internal sealed class CharstringContext
{
    /// <summary>The global subroutines (<c>callgsubr</c>).</summary>
    public CffIndex GlobalSubrs { get; init; } = CffIndex.Empty;

    /// <summary>The local subroutines of the glyph's Private DICT (<c>callsubr</c>).</summary>
    public CffIndex LocalSubrs { get; init; } = CffIndex.Empty;

    /// <summary>The width of a CFF charstring that encodes none.</summary>
    public double DefaultWidthX { get; init; }

    /// <summary>The base a CFF charstring's encoded width is relative to.</summary>
    public double NominalWidthX { get; init; }

    /// <summary>
    /// True for CFF2 charstrings: no width operand, a 513-entry stack, and <c>blend</c> and
    /// <c>vsindex</c> available.
    /// </summary>
    public bool IsCff2 { get; init; }

    /// <summary>CFF2: the Private DICT's <c>vsindex</c>, until a charstring <c>vsindex</c> overrides it.</summary>
    public int VsIndex { get; init; }

    /// <summary>CFF2: region count per ItemVariationData, so <c>blend</c> knows its operand count.</summary>
    public IReadOnlyList<int> RegionCounts { get; init; } = [];

    /// <summary>
    /// CFF2: the scalar for each region of a given <c>vsindex</c> at the current instance, in the
    /// ItemVariationData's region order. A null delegate, or a null result, renders the default
    /// instance (every delta ignored). <c>CffOutlineDecoder</c> supplies it.
    /// </summary>
    public Func<int, double[]?>? BlendScalars { get; init; }

    /// <summary>
    /// CFF <c>seac</c>: the charstring of the glyph a Standard Encoding code names, or null when
    /// the font has no such glyph. Null disables <c>seac</c> entirely (CID-keyed and CFF2 fonts).
    /// </summary>
    public Func<int, ReadOnlyMemory<byte>?>? SeacComponent { get; init; }
}
