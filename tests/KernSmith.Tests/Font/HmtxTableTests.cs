using System.Buffers.Binary;
using KernSmith.Font.Tables;
using Shouldly;

namespace KernSmith.Tests.Font;

public class HmtxTableTests
{
    /// <summary>
    /// Builds a synthetic hmtx buffer: numberOfHMetrics full (advanceWidth uint16, lsb int16)
    /// pairs, followed by trailingLsbCount additional lsb-only int16 entries.
    /// </summary>
    private static byte[] BuildHmtx((ushort advanceWidth, short lsb)[] fullMetrics, short[] trailingLsb)
    {
        var bytes = new byte[fullMetrics.Length * 4 + trailingLsb.Length * 2];
        var offset = 0;
        foreach (var (advanceWidth, lsb) in fullMetrics)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), advanceWidth);
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset + 2), lsb);
            offset += 4;
        }
        foreach (var lsb in trailingLsb)
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), lsb);
            offset += 2;
        }

        return bytes;
    }

    [Fact]
    public void Parse_ReadsFullMetricsPairs()
    {
        // Arrange
        var data = BuildHmtx(
            fullMetrics: new (ushort, short)[] { (600, 10), (700, 20) },
            trailingLsb: Array.Empty<short>());

        // Act
        var table = HmtxTable.Parse(data, numberOfHMetrics: 2);

        // Assert
        table.GetAdvanceWidth(0).ShouldBe((ushort)600);
        table.GetLeftSideBearing(0).ShouldBe((short)10);
        table.GetAdvanceWidth(1).ShouldBe((ushort)700);
        table.GetLeftSideBearing(1).ShouldBe((short)20);
    }

    [Fact]
    public void Parse_TailCompression_ReusesLastAdvanceWidth_ButKeepsOwnLeftSideBearing()
    {
        // Arrange — 2 full metrics, then 2 glyphs beyond numberOfHMetrics that only
        // store their own left side bearing and must reuse the last advance width (700).
        var data = BuildHmtx(
            fullMetrics: new (ushort, short)[] { (600, 10), (700, 20) },
            trailingLsb: new short[] { 30, 40 });

        // Act
        var table = HmtxTable.Parse(data, numberOfHMetrics: 2);

        // Assert
        table.GetAdvanceWidth(2).ShouldBe((ushort)700, "glyphs beyond numberOfHMetrics reuse the last stored advance width");
        table.GetLeftSideBearing(2).ShouldBe((short)30);
        table.GetAdvanceWidth(3).ShouldBe((ushort)700, "glyphs beyond numberOfHMetrics reuse the last stored advance width");
        table.GetLeftSideBearing(3).ShouldBe((short)40);
    }

    [Fact]
    public void GlyphCount_ReflectsFullMetricsPlusTrailingLsbEntries()
    {
        // Arrange
        var data = BuildHmtx(
            fullMetrics: new (ushort, short)[] { (600, 10), (700, 20) },
            trailingLsb: new short[] { 30, 40, 50 });

        // Act
        var table = HmtxTable.Parse(data, numberOfHMetrics: 2);

        // Assert
        table.GlyphCount.ShouldBe(5);
    }
}
