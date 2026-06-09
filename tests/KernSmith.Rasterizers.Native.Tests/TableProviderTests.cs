using KernSmith.Rasterizers.Native.Internal;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

public class TableProviderTests
{
    [Fact]
    public void Parse_Roboto_FindsRequiredTables()
    {
        var provider = TableProvider.Parse(TestFonts.RobotoRegularBytes());

        foreach (var tag in new[] { "head", "cmap", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "glyf", "loca" })
            provider.HasTable(tag).ShouldBeTrue($"expected table '{tag}'");

        provider.IsCff.ShouldBeFalse();
    }

    [Fact]
    public void TryGetTable_ReturnsBytesInBounds()
    {
        var fontBytes = TestFonts.RobotoRegularBytes();
        var provider = TableProvider.Parse(fontBytes);

        var head = provider.TryGetTable("head");
        head.ShouldNotBeNull();
        head!.Value.Length.ShouldBeGreaterThan(0);

        var record = provider.TryGetRecord("head");
        record.ShouldNotBeNull();
        head.Value.Length.ShouldBe((int)record!.Value.Length);
    }

    [Fact]
    public void TryGetTable_ReturnsNullForMissingTable()
    {
        var provider = TableProvider.Parse(TestFonts.RobotoRegularBytes());
        provider.TryGetTable("ZZZZ").ShouldBeNull();
    }

    [Fact]
    public void Parse_InvalidSfntVersion_Throws()
    {
        var bytes = new byte[16];
        bytes[0] = 0xDE; bytes[1] = 0xAD; bytes[2] = 0xBE; bytes[3] = 0xEF;
        Should.Throw<FontFormatException>(() => TableProvider.Parse(bytes));
    }

    [Fact]
    public void Parse_TruncatedHeader_Throws()
    {
        var bytes = new byte[] { 0x00, 0x01 };
        Should.Throw<FontFormatException>(() => TableProvider.Parse(bytes));
    }

    [Fact]
    public void Parse_TruncatedTableData_ThrowsOnAccess()
    {
        // Build a minimal valid offset table whose single table record points past EOF.
        var bytes = new byte[12 + 16];
        // sfnt version 0x00010000 (big-endian: byte[1] = 0x01)
        bytes[1] = 0x01;
        // numTables = 1
        bytes[5] = 0x01;
        // table record at offset 12: tag "head"
        bytes[12] = (byte)'h'; bytes[13] = (byte)'e'; bytes[14] = (byte)'a'; bytes[15] = (byte)'d';
        // checksum (12-15 reused slots are tag) -> checksum at 16..19 = 0
        // offset at 20..23 = 1000 (way past EOF)
        bytes[20] = 0x00; bytes[21] = 0x00; bytes[22] = 0x03; bytes[23] = 0xE8;
        // length at 24..27 = 50
        bytes[27] = 50;

        var provider = TableProvider.Parse(bytes);
        Should.Throw<FontFormatException>(() => provider.TryGetTable("head"));
    }
}
