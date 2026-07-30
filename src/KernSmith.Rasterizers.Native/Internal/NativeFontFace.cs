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
        CmapTable cmap,
        LocaTable? loca,
        GlyfTable? glyf)
    {
        Tables = tables;
        Head = head;
        Hhea = hhea;
        Maxp = maxp;
        Hmtx = hmtx;
        Os2 = os2;
        Cmap = cmap;
        Loca = loca;
        Glyf = glyf;
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

    /// <summary>The parsed <c>loca</c> table, or null for CFF fonts (which have no <c>glyf</c>).</summary>
    public LocaTable? Loca { get; }

    /// <summary>The <c>glyf</c> outline table, or null for CFF fonts.</summary>
    public GlyfTable? Glyf { get; }

    /// <summary>True when this face carries TrueType (<c>glyf</c>) outlines.</summary>
    public bool HasGlyfOutlines => Glyf is not null;

    /// <summary>Maps a Unicode codepoint to its glyph index (0 when unmapped).</summary>
    public int GetGlyphIndex(int codepoint) => Cmap.GetGlyphIndex(codepoint);

    /// <summary>
    /// Decodes a glyph outline in font design units, with composites resolved.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If the glyph index is outside the font.</exception>
    /// <exception cref="FontFormatException">
    /// If the font has no <c>glyf</c> table (CFF outlines are parsed in a later phase),
    /// or the glyph's data is malformed.
    /// </exception>
    public ParsedGlyph GetGlyph(int glyphIndex) =>
        Glyf?.GetGlyph(glyphIndex)
        ?? throw new FontFormatException("This font uses CFF outlines, which have no 'glyf' table.");

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

        // CFF faces have no glyf/loca; their outlines are parsed in a later phase.
        LocaTable? loca = null;
        GlyfTable? glyf = null;
        if (!tables.IsCff)
        {
            loca = LocaTable.Parse(tables.GetTable("loca").Span, maxp.NumGlyphs, head.LongLocaFormat);
            glyf = new GlyfTable(tables.GetTable("glyf"), loca, maxp.ComponentDepthLimit);
        }

        return new NativeFontFace(tables, head, hhea, maxp, hmtx, os2, cmap, loca, glyf);
    }
}
