using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;
using static KernSmith.Rasterizers.Native.Tests.SyntheticColorTables;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// Phase 172 — the <c>CPAL</c> and <c>COLR</c> parsers, on hand-built tables, on the committed
/// fixtures, and on malformed data.
/// </summary>
public class ColorTableTests
{
    // ---- CPAL ------------------------------------------------------------------------------

    [Fact]
    public void Cpal_ReadsPalettesAsRgba()
    {
        var cpal = CpalTable.Parse(Cpal([[0x11223344u, 0x55667788u], [0x99AABBCCu, 0xDDEEFF00u]]));

        cpal.Version.ShouldBe(0);
        cpal.PaletteCount.ShouldBe(2);
        cpal.EntryCount.ShouldBe(2);
        // Stored BGRA on disk, returned as straight RGBA.
        cpal.GetColor(0, 0).ShouldBe(new PaletteColor(0x11, 0x22, 0x33, 0x44));
        cpal.GetColor(0, 1).ShouldBe(new PaletteColor(0x55, 0x66, 0x77, 0x88));
        cpal.GetColor(1, 0).ShouldBe(new PaletteColor(0x99, 0xAA, 0xBB, 0xCC));
        cpal.GetColor(1, 1).ShouldBe(new PaletteColor(0xDD, 0xEE, 0xFF, 0x00));
        cpal.GetPaletteType(0).ShouldBe(0u, "a version 0 table has no palette types");
    }

    [Fact]
    public void Cpal_Version1_ReadsPaletteTypes()
    {
        var cpal = CpalTable.Parse(Cpal([[0xFF0000FFu], [0x00FF00FFu]], [CpalTable.UsableWithLightBackground, CpalTable.UsableWithDarkBackground]));

        cpal.Version.ShouldBe(1);
        cpal.GetPaletteType(0).ShouldBe(CpalTable.UsableWithLightBackground);
        cpal.GetPaletteType(1).ShouldBe(CpalTable.UsableWithDarkBackground);
    }

    [Fact]
    public void Cpal_PalettesMayShareColorRecords()
    {
        // Two palettes pointing at the same first record: legal, and both read the same colors.
        var table = Cpal([[0x10203040u, 0x50607080u], [0x90A0B0C0u, 0xD0E0F000u]]);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(14), 0); // colorRecordIndices[1] = 0

        var cpal = CpalTable.Parse(table);

        cpal.GetColor(1, 0).ShouldBe(cpal.GetColor(0, 0));
        cpal.GetColor(1, 1).ShouldBe(cpal.GetColor(0, 1));
    }

    [Fact]
    public void Cpal_BungeeColorFixture_HasNinePalettesOfTwoEntries()
    {
        var cpal = CpalTable.Parse(Table(TestFonts.BungeeColorBytes(), "CPAL"));

        cpal.PaletteCount.ShouldBe(9);
        cpal.EntryCount.ShouldBe(2);
        cpal.GetColor(0, 0).ShouldBe(new PaletteColor(0xC9, 0x09, 0x00, 0xFF));
        cpal.GetColor(0, 1).ShouldBe(new PaletteColor(0xFF, 0x95, 0x80, 0xFF));
        cpal.GetColor(8, 0).ShouldBe(new PaletteColor(0xFF, 0xE1, 0x0B, 0xFF));
    }

    [Fact]
    public void Cpal_Truncated_Throws()
    {
        var table = Cpal([[0xFF0000FFu]]);
        Should.Throw<FontFormatException>(() => CpalTable.Parse(table.AsSpan(0, 6)));
    }

    [Fact]
    public void Cpal_ColorRecordsPastTheEndOfTheTable_Throws()
    {
        var table = Cpal([[0xFF0000FFu, 0x00FF00FFu]]);
        Should.Throw<FontFormatException>(() => CpalTable.Parse(table.AsSpan(0, table.Length - 1)));
    }

    [Fact]
    public void Cpal_PaletteRunningPastTheColorRecords_Throws()
    {
        // colorRecordIndices[1] + numPaletteEntries must not exceed numColorRecords.
        var table = Cpal([[0xFF0000FFu, 0x00FF00FFu], [0x0000FFFFu, 0xFFFFFFFFu]]);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(14), 3);

        Should.Throw<FontFormatException>(() => CpalTable.Parse(table));
    }

    [Fact]
    public void Cpal_WithNoPalettes_Throws()
    {
        var table = Cpal([[0xFF0000FFu]]);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), 0); // numPalettes

        Should.Throw<FontFormatException>(() => CpalTable.Parse(table));
    }

    [Fact]
    public void Cpal_PaletteTypesPastTheEndOfTheTable_Throws()
    {
        var table = Cpal([[0xFF0000FFu]], [CpalTable.UsableWithDarkBackground]);
        Should.Throw<FontFormatException>(() => CpalTable.Parse(table.AsSpan(0, table.Length - 2)));
    }

    [Fact]
    public void Cpal_UnknownVersion_Throws()
    {
        var table = Cpal([[0xFF0000FFu]]);
        BinaryPrimitives.WriteUInt16BigEndian(table, 2);

        Should.Throw<FontFormatException>(() => CpalTable.Parse(table));
    }

    // ---- COLR ------------------------------------------------------------------------------

    [Fact]
    public void Colr_FindsEachBaseGlyphsLayersInOrder()
    {
        var colr = ColrTable.Parse(Colr(
            (40, [new(7, 1), new(8, 0)]),
            (10, [new(3, 0)]),
            (25, [new(4, Foreground), new(5, 1), new(6, 0)])));

        colr.Version.ShouldBe(0);
        colr.BaseGlyphCount.ShouldBe(3);
        colr.LayerCount.ShouldBe(6);

        Layers(colr, 10).ShouldBe([new ColrLayerRecord(3, 0)]);
        Layers(colr, 25).ShouldBe([new ColrLayerRecord(4, 0xFFFF), new ColrLayerRecord(5, 1), new ColrLayerRecord(6, 0)]);
        Layers(colr, 40).ShouldBe([new ColrLayerRecord(7, 1), new ColrLayerRecord(8, 0)]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(41)]
    [InlineData(65535)]
    public void Colr_GlyphWithoutARecord_IsNotFound(int glyphIndex)
    {
        var colr = ColrTable.Parse(Colr((10, [new(3, 0)]), (25, [new(4, 0)]), (40, [new(7, 1)])));
        colr.TryGetLayerRange(glyphIndex, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Colr_BungeeColorFixture_IsVersion0WithTwoLayersPerGlyph()
    {
        var bytes = TestFonts.BungeeColorBytes();
        var colr = ColrTable.Parse(Table(bytes, "COLR"));
        var face = NativeFontFace.Load(bytes);

        colr.Version.ShouldBe(0);
        colr.TryGetLayerRange(face.GetGlyphIndex('A'), out _, out int count).ShouldBeTrue();
        count.ShouldBe(2);
    }

    [Fact]
    public void Colr_ReemKufiInkFixture_IsVersion1WithNoVersion0Records()
    {
        var bytes = TestFonts.ReemKufiInkBytes();
        var colr = ColrTable.Parse(Table(bytes, "COLR"));
        var face = NativeFontFace.Load(bytes);

        colr.Version.ShouldBe(1);
        colr.BaseGlyphCount.ShouldBe(0);
        colr.TryGetLayerRange(face.GetGlyphIndex('A'), out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Colr_Truncated_Throws()
    {
        var table = Colr((10, [new(3, 0)]));
        Should.Throw<FontFormatException>(() => ColrTable.Parse(table.AsSpan(0, 12)));
    }

    [Fact]
    public void Colr_BaseGlyphRecordsPastTheEndOfTheTable_Throws()
    {
        var table = Colr((10, [new(3, 0)]));
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4), (uint)table.Length - 2);

        Should.Throw<FontFormatException>(() => ColrTable.Parse(table));
    }

    [Fact]
    public void Colr_LayerRecordsPastTheEndOfTheTable_Throws()
    {
        var table = Colr((10, [new(3, 0), new(4, 0)]));
        Should.Throw<FontFormatException>(() => ColrTable.Parse(table.AsSpan(0, table.Length - 1)));
    }

    [Fact]
    public void Colr_HugeOffset_ThrowsRatherThanOverflowing()
    {
        var table = Colr((10, [new(3, 0)]));
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(8), 0xFFFFFFF0u);

        Should.Throw<FontFormatException>(() => ColrTable.Parse(table));
    }

    [Fact]
    public void Colr_UnknownVersion_Throws()
    {
        var table = Colr((10, [new(3, 0)]));
        BinaryPrimitives.WriteUInt16BigEndian(table, 2);

        Should.Throw<FontFormatException>(() => ColrTable.Parse(table));
    }

    [Fact]
    public void Colr_LayerRangePastTheLayerArray_IsReportedAsIs()
    {
        // The parser hands back what the record says; resolving layers stops at the end of the
        // layer array (ColorGlyphs), so a bad range never indexes out of bounds.
        var table = Colr((10, [new(3, 0)]));
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(14 + 4), 5); // numLayers

        var colr = ColrTable.Parse(table);
        colr.TryGetLayerRange(10, out int first, out int count).ShouldBeTrue();
        first.ShouldBe(0);
        count.ShouldBe(5);
        Should.Throw<ArgumentOutOfRangeException>(() => colr.GetLayer(1));
    }

    // ---- COLR version 1 --------------------------------------------------------------------

    [Fact]
    public void ColrV1_ReemKufiInkFixture_ListsItsPaintRecordsLayersAndClips()
    {
        var bytes = TestFonts.ReemKufiInkBytes();
        var table = ColrV1Table.Parse(Table(bytes, "COLR"))!;
        var face = NativeFontFace.Load(bytes);

        table.BaseGlyphCount.ShouldBe(167);
        table.LayerCount.ShouldBe(311);
        table.ClipCount.ShouldBeGreaterThan(0);
        table.VariationStore.ShouldBeNull();

        int one = face.GetGlyphIndex(0x0661);
        table.TryGetBasePaint(one, out long paint).ShouldBeTrue();
        table.Data.Span[(int)paint].ShouldBe((byte)10, "one-ar is a PaintGlyph");
        table.TryGetClipBox(one, out var box).ShouldBeTrue();
        box.ShouldBe(new ColrClipBox(200, 0, 300, 600, ColrV1Table.NoVariation));
        table.TryGetBasePaint(face.GetGlyphIndex('A'), out _).ShouldBeFalse();
    }

    [Fact]
    public void ColrV1_Version0Table_IsNotVersion1()
    {
        ColrV1Table.Parse(Colr((10, [new(3, 0)]))).ShouldBeNull();
    }

    [Fact]
    public void ColrV1_ClipBoxes_AreFoundByGlyphRange()
    {
        var table = ColrV1Table.Parse(SyntheticColrV1.Colr(
            [(5, new SyntheticColrV1.Solid(0))],
            [new SyntheticColrV1.Clip(3, 6, 1, 2, 3, 4), new SyntheticColrV1.Clip(10, 10, 5, 6, 7, 8, Var: 9)]))!;

        table.TryGetClipBox(2, out _).ShouldBeFalse();
        table.TryGetClipBox(3, out var a).ShouldBeTrue();
        table.TryGetClipBox(6, out var b).ShouldBeTrue();
        a.ShouldBe(new ColrClipBox(1, 2, 3, 4, ColrV1Table.NoVariation));
        b.ShouldBe(a);
        table.TryGetClipBox(7, out _).ShouldBeFalse();
        table.TryGetClipBox(10, out var c).ShouldBeTrue();
        c.ShouldBe(new ColrClipBox(5, 6, 7, 8, 9));
    }

    [Theory]
    [InlineData(14)]    // BaseGlyphList
    [InlineData(18)]    // LayerList
    [InlineData(22)]    // ClipList
    public void ColrV1_ListPastTheEndOfTheTable_Throws(int headerField)
    {
        var table = SyntheticColrV1.Colr(
            [(5, new SyntheticColrV1.Layers(new SyntheticColrV1.Solid(0)))],
            [new SyntheticColrV1.Clip(5, 5, 0, 0, 10, 10)]);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(headerField), (uint)table.Length - 2);

        Should.Throw<FontFormatException>(() => ColrV1Table.Parse(table));
    }

    [Fact]
    public void ColrV1_TruncatedHeader_Throws()
    {
        var table = SyntheticColrV1.Colr(5, new SyntheticColrV1.Solid(0));
        Should.Throw<FontFormatException>(() => ColrV1Table.Parse(table.AsMemory(0, 20)));
    }

    [Fact]
    public void ColrV1_HugeRecordCount_ThrowsRatherThanOverflowing()
    {
        var table = SyntheticColrV1.Colr(5, new SyntheticColrV1.Solid(0));
        int list = (int)BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(14));
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(list), 0xFFFFFFFF);

        Should.Throw<FontFormatException>(() => ColrV1Table.Parse(table));
    }

    private static ColrLayerRecord[] Layers(ColrTable colr, int glyphIndex)
    {
        colr.TryGetLayerRange(glyphIndex, out int first, out int count).ShouldBeTrue();
        return Enumerable.Range(first, count).Select(colr.GetLayer).ToArray();
    }

    private static byte[] Table(byte[] font, string tag) =>
        TableProvider.Parse(font).GetTable(tag).ToArray();
}
