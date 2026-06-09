using KernSmith.Rasterizers.Native.Internal.Tables;

namespace KernSmith.Rasterizers.Native.Internal;

/// <summary>
/// A fully parsed font face: the table directory plus the core tables the native
/// rasterizer needs. Built once when a font is loaded and reused for every glyph.
/// </summary>
internal sealed class NativeFontFace
{
    private NativeFontFace(
        TableProvider tables,
        HeadTable head,
        HheaTable hhea,
        MaxpTable maxp,
        HmtxTable hmtx,
        Os2Table os2,
        CmapTable cmap)
    {
        Tables = tables;
        Head = head;
        Hhea = hhea;
        Maxp = maxp;
        Hmtx = hmtx;
        Os2 = os2;
        Cmap = cmap;
    }

    /// <summary>Lazy access to the raw table bytes by tag.</summary>
    public TableProvider Tables { get; }

    /// <summary>The parsed <c>head</c> table.</summary>
    public HeadTable Head { get; }

    /// <summary>The parsed <c>hhea</c> table.</summary>
    public HheaTable Hhea { get; }

    /// <summary>The parsed <c>maxp</c> table.</summary>
    public MaxpTable Maxp { get; }

    /// <summary>The parsed <c>hmtx</c> table.</summary>
    public HmtxTable Hmtx { get; }

    /// <summary>The parsed <c>OS/2</c> table.</summary>
    public Os2Table Os2 { get; }

    /// <summary>The parsed <c>cmap</c> table.</summary>
    public CmapTable Cmap { get; }

    /// <summary>Maps a Unicode codepoint to its glyph index (0 when unmapped).</summary>
    public int GetGlyphIndex(int codepoint) => Cmap.GetGlyphIndex(codepoint);

    /// <summary>
    /// Parses a font face from raw bytes: validates the table directory and parses the
    /// core tables required for rasterization.
    /// </summary>
    /// <param name="fontData">The full font file bytes.</param>
    /// <param name="faceIndex">Which face to load from a TrueType Collection. Usually 0.</param>
    /// <exception cref="FontFormatException">If the font is invalid or missing required tables.</exception>
    public static NativeFontFace Load(ReadOnlyMemory<byte> fontData, int faceIndex = 0)
    {
        var tables = TableProvider.Parse(fontData, faceIndex);
        FontValidator.Validate(tables);

        var head = HeadTable.Parse(tables.GetTable("head").Span);
        var hhea = HheaTable.Parse(tables.GetTable("hhea").Span);
        var maxp = MaxpTable.Parse(tables.GetTable("maxp").Span);
        var hmtx = HmtxTable.Parse(tables.GetTable("hmtx").Span, hhea.NumberOfHMetrics, maxp.NumGlyphs);
        var os2 = Os2Table.Parse(tables.GetTable("OS/2").Span);
        var cmap = CmapTable.Parse(tables.GetTable("cmap").Span);

        return new NativeFontFace(tables, head, hhea, maxp, hmtx, os2, cmap);
    }
}
