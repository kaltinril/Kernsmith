using System.Buffers.Binary;
using KernSmith.Rasterizers.Native.Internal;
using KernSmith.Rasterizers.Native.Internal.Tables;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Phase 168 — the <c>post</c> table header.</summary>
public class PostTableTests
{
    [Fact]
    public void Roboto_ParsesTheHeader()
    {
        // Expected values read independently from the file's raw bytes.
        var post = PostTable.Parse(TableProvider.Parse(TestFonts.RobotoRegularBytes()).GetTable("post").Span);

        post.ShouldBe(new PostTable(0x00030000, 0f, -150, 100, false));
    }

    [Fact]
    public void CffFace_ParsesTheSameHeader()
    {
        // post is outline-format independent: an OTTO face's table parses the same way.
        var post = PostTable.Parse(TableProvider.Parse(SyntheticFonts.CffFlavoured()).GetTable("post").Span);

        post.ShouldBe(new PostTable(0x00030000, 0f, -150, 100, false));
    }

    [Fact]
    public void NegativeItalicAngle_AndFixedPitch_Parse()
    {
        var data = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(data, 0x00020000);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), -12 * 65536 - 32768); // -12.5°
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(8), -200);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(10), 50);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 1);

        PostTable.Parse(data).ShouldBe(new PostTable(0x00020000, -12.5f, -200, 50, true));
    }

    [Fact]
    public void TruncatedHeader_IsAFormatError()
    {
        Should.Throw<FontFormatException>(() => PostTable.Parse(new byte[10]));
    }
}
