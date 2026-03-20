using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bmfontier.Output.Model;

namespace Bmfontier.Output;

/// <summary>
/// Formats a <see cref="BmFontModel"/> as BMFont binary format (version 3).
/// </summary>
internal sealed class BmFontBinaryFormatter : IBmFontBinaryFormatter
{
    public string Format => "binary";

    public byte[] FormatBinary(BmFontModel model)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);

        // Header: "BMF" + version 3
        writer.Write((byte)66); // B
        writer.Write((byte)77); // M
        writer.Write((byte)70); // F
        writer.Write((byte)3);  // version

        WriteInfoBlock(writer, model.Info);
        WriteCommonBlock(writer, model.Common);
        WritePagesBlock(writer, model.Pages);
        WriteCharsBlock(writer, model.Characters);

        if (model.KerningPairs.Count > 0)
            WriteKerningBlock(writer, model.KerningPairs);

        if (model.Extended != null)
            WriteExtendedBlock(writer, model.Extended);

        writer.Flush();
        return ms.ToArray();
    }

    private static void WriteBlock(BinaryWriter writer, byte blockType, byte[] data)
    {
        writer.Write(blockType);
        writer.Write(data.Length); // int32, little-endian
        writer.Write(data);
    }

    private static void WriteInfoBlock(BinaryWriter writer, InfoBlock info)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8);

        bw.Write((short)info.Size);      // int16 fontSize

        byte bitField = 0;
        if (info.Smooth) bitField |= 1 << 7;
        if (info.Unicode) bitField |= 1 << 6;
        if (info.Italic) bitField |= 1 << 5;
        if (info.Bold) bitField |= 1 << 4;
        if (info.FixedHeight) bitField |= 1 << 3;
        bw.Write(bitField);               // uint8 bitField

        bw.Write((byte)0);                // uint8 charSet
        bw.Write((ushort)info.StretchH);  // uint16 stretchH
        bw.Write((byte)info.Aa);          // uint8 aa
        bw.Write((byte)info.Padding.Up);
        bw.Write((byte)info.Padding.Right);
        bw.Write((byte)info.Padding.Down);
        bw.Write((byte)info.Padding.Left);
        bw.Write((byte)info.Spacing.Horizontal);
        bw.Write((byte)info.Spacing.Vertical);
        bw.Write((byte)0);                // uint8 outline

        // Null-terminated font name (UTF-8)
        var nameBytes = Encoding.UTF8.GetBytes(info.Face);
        bw.Write(nameBytes);
        bw.Write((byte)0);

        bw.Flush();
        WriteBlock(writer, 1, ms.ToArray());
    }

    private static void WriteCommonBlock(BinaryWriter writer, CommonBlock common)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8);

        bw.Write((ushort)common.LineHeight);
        bw.Write((ushort)common.Base);
        bw.Write((ushort)common.ScaleW);
        bw.Write((ushort)common.ScaleH);
        bw.Write((ushort)common.Pages);

        byte bitField = 0;
        if (common.Packed) bitField |= 1 << 7;
        bw.Write(bitField);

        bw.Write((byte)common.AlphaChnl);
        bw.Write((byte)common.RedChnl);
        bw.Write((byte)common.GreenChnl);
        bw.Write((byte)common.BlueChnl);

        bw.Flush();
        WriteBlock(writer, 2, ms.ToArray());
    }

    private static void WritePagesBlock(BinaryWriter writer, IReadOnlyList<PageEntry> pages)
    {
        if (pages.Count == 0)
        {
            WriteBlock(writer, 3, Array.Empty<byte>());
            return;
        }

        // All page name strings are padded to the same length (the longest + null terminator).
        var maxLen = 0;
        var encodedNames = new byte[pages.Count][];
        for (var i = 0; i < pages.Count; i++)
        {
            encodedNames[i] = Encoding.UTF8.GetBytes(pages[i].File);
            if (encodedNames[i].Length > maxLen)
                maxLen = encodedNames[i].Length;
        }

        var stringLen = maxLen + 1; // including null terminator
        var data = new byte[pages.Count * stringLen];

        for (var i = 0; i < pages.Count; i++)
        {
            Buffer.BlockCopy(encodedNames[i], 0, data, i * stringLen, encodedNames[i].Length);
            // Remaining bytes are already 0 (null padding)
        }

        WriteBlock(writer, 3, data);
    }

    private static void WriteCharsBlock(BinaryWriter writer, IReadOnlyList<CharEntry> chars)
    {
        using var ms = new MemoryStream(chars.Count * 20);
        using var bw = new BinaryWriter(ms, Encoding.UTF8);

        foreach (var ch in chars)
        {
            bw.Write((uint)ch.Id);
            bw.Write((ushort)ch.X);
            bw.Write((ushort)ch.Y);
            bw.Write((ushort)ch.Width);
            bw.Write((ushort)ch.Height);
            bw.Write((short)ch.XOffset);
            bw.Write((short)ch.YOffset);
            bw.Write((short)ch.XAdvance);
            bw.Write((byte)ch.Page);
            bw.Write((byte)ch.Channel);
        }

        bw.Flush();
        WriteBlock(writer, 4, ms.ToArray());
    }

    private static void WriteKerningBlock(BinaryWriter writer, IReadOnlyList<KerningEntry> kernings)
    {
        using var ms = new MemoryStream(kernings.Count * 10);
        using var bw = new BinaryWriter(ms, Encoding.UTF8);

        foreach (var kern in kernings)
        {
            bw.Write((uint)kern.First);
            bw.Write((uint)kern.Second);
            bw.Write((short)kern.Amount);
        }

        bw.Flush();
        WriteBlock(writer, 5, ms.ToArray());
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static void WriteExtendedBlock(BinaryWriter writer, ExtendedMetadata extended)
    {
        // Build a dictionary of non-null fields for clean JSON output.
        var dict = new Dictionary<string, object>
        {
            ["version"] = extended.GeneratorVersion
        };

        if (extended.SdfSpread.HasValue) dict["sdfSpread"] = extended.SdfSpread.Value;
        if (extended.OutlineThickness.HasValue) dict["outlineThickness"] = extended.OutlineThickness.Value;
        if (extended.GradientTopColor != null) dict["gradientTopColor"] = extended.GradientTopColor;
        if (extended.GradientBottomColor != null) dict["gradientBottomColor"] = extended.GradientBottomColor;
        if (extended.ShadowOffsetX.HasValue) dict["shadowOffsetX"] = extended.ShadowOffsetX.Value;
        if (extended.ShadowOffsetY.HasValue) dict["shadowOffsetY"] = extended.ShadowOffsetY.Value;
        if (extended.ShadowColor != null) dict["shadowColor"] = extended.ShadowColor;
        if (extended.SuperSampleLevel.HasValue) dict["superSampleLevel"] = extended.SuperSampleLevel.Value;
        if (extended.ColorFont is true) dict["colorFont"] = true;
        if (extended.VariationAxes is { Count: > 0 }) dict["variationAxes"] = extended.VariationAxes;

        var json = JsonSerializer.Serialize(dict, JsonOptions);
        var jsonBytes = Encoding.UTF8.GetBytes(json);

        // Null-terminated UTF-8 JSON payload
        var payload = new byte[jsonBytes.Length + 1];
        Buffer.BlockCopy(jsonBytes, 0, payload, 0, jsonBytes.Length);
        // payload[^1] is already 0

        WriteBlock(writer, 6, payload);
    }
}
