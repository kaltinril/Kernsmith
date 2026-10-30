namespace KernSmith.Rasterizers.Native.Internal.Cff;

/// <summary>
/// A PostScript FontMatrix <c>[a b c d e f]</c>: <c>x' = a·x + c·y + e</c>, <c>y' = b·x + d·y + f</c>,
/// mapping charstring units to the em square (so the usual matrix is 0.001 for a 1000-unit em).
/// </summary>
internal readonly record struct CffMatrix(double A, double B, double C, double D, double E, double F)
{
    /// <summary>
    /// The FontMatrix a glyph is drawn with. A CID-keyed Font DICT's matrix is concatenated with
    /// the Top DICT's only when both set one; otherwise whichever is set applies.
    /// </summary>
    /// <returns>The matrix, or null when neither DICT sets one.</returns>
    /// <exception cref="FontFormatException">If a matrix does not have six operands.</exception>
    public static CffMatrix? From(double[]? top, double[]? fontDict)
    {
        var topMatrix = Parse(top);
        var fdMatrix = Parse(fontDict);

        if (topMatrix is { } t && fdMatrix is { } f)
            return f.Then(t);

        return fdMatrix ?? topMatrix;
    }

    /// <summary>
    /// The transform from charstring units to font design units, or null when it is the
    /// identity (the common case).
    /// </summary>
    /// <remarks>
    /// In an OpenType font, charstring units are font units: <c>head.unitsPerEm</c> sets the em,
    /// and the FontMatrix's absolute scale does not. Only its shape survives — the matrix is
    /// normalized so its y scale is 1 (falling back to the y-from-x term when that is 0), its
    /// translation taken in the same normalized units. This is FreeType's reading, so a
    /// 2048-unit font whose FontMatrix says 0.001 renders the same size on both backends, and a
    /// matrix that stretches x relative to y stretches it on both.
    /// </remarks>
    /// <exception cref="FontFormatException">If the matrix has no y scale at all.</exception>
    public static CffMatrix? ToFontUnits(CffMatrix? matrix)
    {
        if (matrix is not { } m)
            return null;

        double yScale = !m.D.Equals(0d) ? Math.Abs(m.D) : Math.Abs(m.B);
        if (yScale.Equals(0d) || !double.IsFinite(1 / yScale))
            throw new FontFormatException("CFF FontMatrix has no y scale.");

        var scaled = new CffMatrix(m.A / yScale, m.B / yScale, m.C / yScale, m.D / yScale, m.E / yScale, m.F / yScale);

        const double Tolerance = 1e-6;
        bool identity = Math.Abs(scaled.A - 1) < Tolerance && Math.Abs(scaled.D - 1) < Tolerance
            && Math.Abs(scaled.B) < Tolerance && Math.Abs(scaled.C) < Tolerance
            && Math.Abs(scaled.E) < Tolerance && Math.Abs(scaled.F) < Tolerance;

        return identity ? null : scaled;
    }

    /// <summary>Applies this matrix to a point.</summary>
    public (double X, double Y) Apply(double x, double y) => ((A * x) + (C * y) + E, (B * x) + (D * y) + F);

    /// <summary>This matrix followed by <paramref name="next"/>.</summary>
    private CffMatrix Then(CffMatrix next) => new(
        (A * next.A) + (B * next.C),
        (A * next.B) + (B * next.D),
        (C * next.A) + (D * next.C),
        (C * next.B) + (D * next.D),
        (E * next.A) + (F * next.C) + next.E,
        (E * next.B) + (F * next.D) + next.F);

    private static CffMatrix? Parse(double[]? operands)
    {
        if (operands is null)
            return null;

        if (operands.Length != 6)
            throw new FontFormatException($"CFF FontMatrix has {operands.Length} operands instead of 6.");

        return new CffMatrix(operands[0], operands[1], operands[2], operands[3], operands[4], operands[5]);
    }
}
