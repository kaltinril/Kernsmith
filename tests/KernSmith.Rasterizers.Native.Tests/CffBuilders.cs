using System.Buffers.Binary;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Type 2 charstring operator bytes, for building test charstrings with <see cref="Cs"/>.</summary>
internal static class T2
{
    public static readonly byte[] HStem = [1];
    public static readonly byte[] VStem = [3];
    public static readonly byte[] VMoveTo = [4];
    public static readonly byte[] RLineTo = [5];
    public static readonly byte[] HLineTo = [6];
    public static readonly byte[] VLineTo = [7];
    public static readonly byte[] RRCurveTo = [8];
    public static readonly byte[] CallSubr = [10];
    public static readonly byte[] Return = [11];
    public static readonly byte[] EndChar = [14];
    public static readonly byte[] VsIndex = [15];
    public static readonly byte[] Blend = [16];
    public static readonly byte[] HStemHm = [18];
    public static readonly byte[] HintMask = [19];
    public static readonly byte[] CntrMask = [20];
    public static readonly byte[] RMoveTo = [21];
    public static readonly byte[] HMoveTo = [22];
    public static readonly byte[] VStemHm = [23];
    public static readonly byte[] RCurveLine = [24];
    public static readonly byte[] RLineCurve = [25];
    public static readonly byte[] VVCurveTo = [26];
    public static readonly byte[] HHCurveTo = [27];
    public static readonly byte[] CallGSubr = [29];
    public static readonly byte[] VHCurveTo = [30];
    public static readonly byte[] HVCurveTo = [31];

    public static readonly byte[] DotSection = [12, 0];
    public static readonly byte[] And = [12, 3];
    public static readonly byte[] Or = [12, 4];
    public static readonly byte[] Not = [12, 5];
    public static readonly byte[] Abs = [12, 9];
    public static readonly byte[] Add = [12, 10];
    public static readonly byte[] Sub = [12, 11];
    public static readonly byte[] Div = [12, 12];
    public static readonly byte[] Neg = [12, 14];
    public static readonly byte[] Eq = [12, 15];
    public static readonly byte[] Drop = [12, 18];
    public static readonly byte[] Put = [12, 20];
    public static readonly byte[] Get = [12, 21];
    public static readonly byte[] IfElse = [12, 22];
    public static readonly byte[] Random = [12, 23];
    public static readonly byte[] Mul = [12, 24];
    public static readonly byte[] Sqrt = [12, 26];
    public static readonly byte[] Dup = [12, 27];
    public static readonly byte[] Exch = [12, 28];
    public static readonly byte[] Index = [12, 29];
    public static readonly byte[] Roll = [12, 30];
    public static readonly byte[] HFlex = [12, 34];
    public static readonly byte[] Flex = [12, 35];
    public static readonly byte[] HFlex1 = [12, 36];
    public static readonly byte[] Flex1 = [12, 37];
}

/// <summary>A number forced into the 5-byte 16.16 fixed encoding (charstring operator 255).</summary>
internal readonly record struct Fixed(double Value);

/// <summary>A number forced into the 3-byte int16 encoding (operator 28).</summary>
internal readonly record struct Short(int Value);

/// <summary>Raw bytes copied verbatim (hint mask data, deliberate garbage).</summary>
internal readonly record struct Raw(params byte[] Bytes);

/// <summary>
/// Builds Type 2 charstrings: ints use the shortest encoding, <see cref="Fixed"/>/<see cref="Short"/>
/// force one, <see cref="T2"/> byte arrays are operators and <see cref="Raw"/> is copied as is.
/// </summary>
internal static class Cs
{
    public static byte[] Of(params object[] parts)
    {
        var bytes = new List<byte>();
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte[] op: bytes.AddRange(op); break;
                case Raw raw: bytes.AddRange(raw.Bytes); break;
                case int value: bytes.AddRange(Number(value)); break;
                case Short s: bytes.Add(28); bytes.Add((byte)(s.Value >> 8)); bytes.Add((byte)s.Value); break;
                case Fixed f:
                    int raw32 = (int)Math.Round(f.Value * 65536);
                    bytes.Add(255);
                    bytes.Add((byte)(raw32 >> 24)); bytes.Add((byte)(raw32 >> 16));
                    bytes.Add((byte)(raw32 >> 8)); bytes.Add((byte)raw32);
                    break;
                default: throw new ArgumentException($"Unsupported charstring part {part}.");
            }
        }

        return [.. bytes];
    }

    /// <summary>The shortest Type 2 / DICT integer encoding below 32768 in magnitude.</summary>
    public static byte[] Number(int value)
    {
        if (value is >= -107 and <= 107)
            return [(byte)(value + 139)];
        if (value is >= 108 and <= 1131)
            return [(byte)(((value - 108) >> 8) + 247), (byte)((value - 108) & 0xFF)];
        if (value is >= -1131 and <= -108)
            return [(byte)(((-value - 108) >> 8) + 251), (byte)((-value - 108) & 0xFF)];
        return [28, (byte)(value >> 8), (byte)value];
    }
}

/// <summary>Builds CFF INDEX structures and DICTs.</summary>
internal static class CffBytes
{
    public static byte[] Index(IReadOnlyList<byte[]> items, bool cff2 = false)
    {
        var bytes = new List<byte>();
        if (cff2)
            bytes.AddRange(BigEndian32((uint)items.Count));
        else
            bytes.AddRange([(byte)(items.Count >> 8), (byte)items.Count]);

        if (items.Count == 0)
            return [.. bytes];

        int total = items.Sum(i => i.Length) + 1;
        int offSize = total < 0x100 ? 1 : total < 0x10000 ? 2 : total < 0x1000000 ? 3 : 4;
        bytes.Add((byte)offSize);

        int offset = 1;
        WriteOffset(offset);
        foreach (var item in items)
        {
            offset += item.Length;
            WriteOffset(offset);
        }

        foreach (var item in items)
            bytes.AddRange(item);

        return [.. bytes];

        void WriteOffset(int value)
        {
            for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
                bytes.Add((byte)(value >> shift));
        }
    }

    /// <summary>A DICT integer operand in the fixed 5-byte form, so layouts can be computed before values are known.</summary>
    public static byte[] Int32(int value) => [29, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    /// <summary>A DICT real operand (nibble-encoded).</summary>
    public static byte[] Real(string text)
    {
        var nibbles = new List<int>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsDigit(c)) nibbles.Add(c - '0');
            else if (c == '.') nibbles.Add(0xA);
            else if (c == '-' && i > 0 && text[i - 1] == 'E') { nibbles.RemoveAt(nibbles.Count - 1); nibbles.Add(0xC); }
            else if (c == 'E') nibbles.Add(0xB);
            else if (c == '-') nibbles.Add(0xE);
        }

        nibbles.Add(0xF);
        if (nibbles.Count % 2 == 1)
            nibbles.Add(0xF);

        var bytes = new List<byte> { 30 };
        for (int i = 0; i < nibbles.Count; i += 2)
            bytes.Add((byte)((nibbles[i] << 4) | nibbles[i + 1]));

        return [.. bytes];
    }

    public static byte[] BigEndian32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}

/// <summary>One Font DICT's Private data in a synthetic CFF.</summary>
internal sealed record SyntheticPrivate(byte[][] LocalSubrs, int DefaultWidthX = 0, int NominalWidthX = 0);

/// <summary>
/// Builds whole <c>CFF </c> tables: name-keyed (one Private DICT) or CID-keyed (FDArray plus an
/// FDSelect of format 0 or 3). Offsets are written in the fixed 5-byte DICT form so the layout
/// can be computed once and filled in.
/// </summary>
internal static class SyntheticCff
{
    public static byte[] NameKeyed(
        IReadOnlyList<byte[]> charStrings,
        SyntheticPrivate privateData,
        IReadOnlyList<byte[]>? globalSubrs = null,
        byte[]? fontMatrix = null) =>
        Build(charStrings, [privateData], fdSelect: null, globalSubrs ?? [], fontMatrix);

    public static byte[] CidKeyed(
        IReadOnlyList<byte[]> charStrings,
        IReadOnlyList<SyntheticPrivate> fontDicts,
        byte[] fdSelect,
        IReadOnlyList<byte[]>? globalSubrs = null) =>
        Build(charStrings, fontDicts, fdSelect, globalSubrs ?? [], fontMatrix: null);

    /// <summary>FDSelect format 0: one Font DICT index per glyph.</summary>
    public static byte[] FdSelectFormat0(params byte[] fdPerGlyph) => [0, .. fdPerGlyph];

    /// <summary>FDSelect format 3 from (first glyph, fd) ranges and the sentinel.</summary>
    public static byte[] FdSelectFormat3(int sentinel, params (int First, int Fd)[] ranges)
    {
        var bytes = new List<byte> { 3, (byte)(ranges.Length >> 8), (byte)ranges.Length };
        foreach (var (first, fd) in ranges)
        {
            bytes.Add((byte)(first >> 8));
            bytes.Add((byte)first);
            bytes.Add((byte)fd);
        }

        bytes.Add((byte)(sentinel >> 8));
        bytes.Add((byte)sentinel);
        return [.. bytes];
    }

    private static byte[] Build(
        IReadOnlyList<byte[]> charStrings,
        IReadOnlyList<SyntheticPrivate> privates,
        byte[]? fdSelect,
        IReadOnlyList<byte[]> globalSubrs,
        byte[]? fontMatrix)
    {
        bool cid = fdSelect is not null;

        // Pass 1 with placeholder offsets fixes every size; pass 2 writes the real offsets.
        var layout = Layout(charStrings, privates, fdSelect, globalSubrs, fontMatrix, cid, default);
        return [.. Layout(charStrings, privates, fdSelect, globalSubrs, fontMatrix, cid, layout.Offsets).Bytes];
    }

    private readonly record struct Offsets(int CharStrings, int FdSelect, int FdArray, int[] Privates, int[] PrivateSizes);

    private static (List<byte> Bytes, Offsets Offsets) Layout(
        IReadOnlyList<byte[]> charStrings,
        IReadOnlyList<SyntheticPrivate> privates,
        byte[]? fdSelect,
        IReadOnlyList<byte[]> globalSubrs,
        byte[]? fontMatrix,
        bool cid,
        Offsets known)
    {
        int[] privateOffsets = known.Privates ?? new int[privates.Count];
        int[] privateSizes = known.PrivateSizes ?? new int[privates.Count];

        var top = new List<byte>();
        if (cid)
        {
            // ROS: Registry/Ordering SIDs (any custom strings) and a supplement.
            top.AddRange([.. Cs.Number(391), .. Cs.Number(392), .. Cs.Number(0), 12, 30]);
        }

        if (fontMatrix is not null)
            top.AddRange(fontMatrix);

        top.AddRange([.. CffBytes.Int32(known.CharStrings), 17]);
        if (cid)
        {
            top.AddRange([.. CffBytes.Int32(known.FdArray), 12, 36]);
            top.AddRange([.. CffBytes.Int32(known.FdSelect), 12, 37]);
        }
        else
        {
            top.AddRange([.. CffBytes.Int32(privateSizes[0]), .. CffBytes.Int32(privateOffsets[0]), 18]);
        }

        var bytes = new List<byte> { 1, 0, 4, 4 };
        bytes.AddRange(CffBytes.Index([[(byte)'T', (byte)'e', (byte)'s', (byte)'t']]));
        bytes.AddRange(CffBytes.Index([[.. top]]));
        bytes.AddRange(CffBytes.Index([[(byte)'A'], [(byte)'B']]));
        bytes.AddRange(CffBytes.Index(globalSubrs));

        int charStringsOffset = bytes.Count;
        bytes.AddRange(CffBytes.Index(charStrings));

        int fdSelectOffset = 0, fdArrayOffset = 0;
        if (cid)
        {
            fdSelectOffset = bytes.Count;
            bytes.AddRange(fdSelect!);

            fdArrayOffset = bytes.Count;
            var fontDicts = new List<byte[]>();
            for (int i = 0; i < privates.Count; i++)
                fontDicts.Add([.. CffBytes.Int32(privateSizes[i]), .. CffBytes.Int32(privateOffsets[i]), 18]);

            bytes.AddRange(CffBytes.Index(fontDicts));
        }

        var newOffsets = new int[privates.Count];
        var newSizes = new int[privates.Count];
        for (int i = 0; i < privates.Count; i++)
        {
            var p = privates[i];
            // Private DICT: widths, then Subrs pointing just past the DICT itself.
            var dict = new List<byte>();
            dict.AddRange([.. CffBytes.Int32(p.DefaultWidthX), 20]);
            dict.AddRange([.. CffBytes.Int32(p.NominalWidthX), 21]);
            dict.AddRange([.. CffBytes.Int32(0), 19]);
            int size = dict.Count;
            dict.RemoveRange(dict.Count - 6, 6);
            dict.AddRange([.. CffBytes.Int32(size), 19]);

            newOffsets[i] = bytes.Count;
            newSizes[i] = size;
            bytes.AddRange(dict);
            bytes.AddRange(CffBytes.Index(p.LocalSubrs));
        }

        return (bytes, new Offsets(charStringsOffset, fdSelectOffset, fdArrayOffset, newOffsets, newSizes));
    }
}
