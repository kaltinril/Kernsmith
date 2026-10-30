using System.Globalization;
using System.Text;

namespace KernSmith.Rasterizers.Native.Internal.Cff;

/// <summary>
/// A parsed CFF/CFF2 DICT (TN#5176 §4): operator → operands. Two-byte operators
/// (<c>12 xx</c>) are keyed <c>1200 + xx</c>, so FontMatrix (<c>12 7</c>) is 1207.
/// </summary>
internal sealed class CffDict
{
    /// <summary>Top DICT <c>charset</c>.</summary>
    public const int Charset = 15;

    /// <summary>Top DICT <c>CharStrings</c>.</summary>
    public const int CharStrings = 17;

    /// <summary>Top/Font DICT <c>Private</c> (size, offset).</summary>
    public const int Private = 18;

    /// <summary>Private DICT <c>Subrs</c> (offset from the Private DICT).</summary>
    public const int Subrs = 19;

    /// <summary>Private DICT <c>defaultWidthX</c>.</summary>
    public const int DefaultWidthX = 20;

    /// <summary>Private DICT <c>nominalWidthX</c>.</summary>
    public const int NominalWidthX = 21;

    /// <summary>CFF2 Private DICT <c>vsindex</c>.</summary>
    public const int VsIndex = 22;

    /// <summary>CFF2 Private DICT <c>blend</c>.</summary>
    public const int Blend = 23;

    /// <summary>CFF2 Top DICT <c>vstore</c>.</summary>
    public const int VariationStore = 24;

    /// <summary>Top DICT <c>CharstringType</c>.</summary>
    public const int CharstringType = 1206;

    /// <summary>Top/Font DICT <c>FontMatrix</c>.</summary>
    public const int FontMatrix = 1207;

    /// <summary>Top DICT <c>ROS</c>; its presence marks a CID-keyed font.</summary>
    public const int Ros = 1230;

    /// <summary>Top DICT <c>FDArray</c>.</summary>
    public const int FdArray = 1236;

    /// <summary>Top DICT <c>FDSelect</c>.</summary>
    public const int FdSelect = 1237;

    private readonly Dictionary<int, double[]> _entries;

    private CffDict(Dictionary<int, double[]> entries)
    {
        _entries = entries;
    }

    /// <summary>True when the DICT sets <paramref name="op"/>.</summary>
    public bool Has(int op) => _entries.ContainsKey(op);

    /// <summary>The operands of <paramref name="op"/>, or null when it is absent.</summary>
    public double[]? Get(int op) => _entries.TryGetValue(op, out var operands) ? operands : null;

    /// <summary>The first operand of <paramref name="op"/>, or <paramref name="fallback"/>.</summary>
    public double GetNumber(int op, double fallback) =>
        _entries.TryGetValue(op, out var operands) && operands.Length > 0 ? operands[0] : fallback;

    /// <summary>
    /// The first operand of <paramref name="op"/> as a byte offset or count, or null when absent.
    /// </summary>
    /// <exception cref="FontFormatException">If the operand is negative or not an integer.</exception>
    public int? GetInt(int op, int operandIndex = 0)
    {
        if (!_entries.TryGetValue(op, out var operands))
            return null;

        if (operandIndex >= operands.Length)
            throw new FontFormatException($"CFF DICT operator {op} is missing operand {operandIndex}.");

        double value = operands[operandIndex];
        if (value < 0 || value > int.MaxValue || !double.IsInteger(value))
            throw new FontFormatException($"CFF DICT operator {op} has invalid operand {value}.");

        return (int)value;
    }

    /// <summary>Parses DICT data.</summary>
    /// <param name="data">The DICT bytes.</param>
    /// <param name="cff2">True for CFF2, which allows a deeper stack plus <c>blend</c>/<c>vsindex</c>.</param>
    /// <param name="regionCount">
    /// CFF2 only: the number of variation regions for a given <c>vsindex</c>, needed to know how many
    /// operands <c>blend</c> consumes. Null when the font has no variation store.
    /// </param>
    /// <remarks>
    /// <c>blend</c> keeps the default values and drops the deltas, at every instance. Only the Private
    /// DICT's hinting values (BlueValues, OtherBlues, StdHW/StdVW, StemSnapH/V) are blendable, and
    /// outlines are drawn without them; a hinter that reads them would need the instance's blend.
    /// </remarks>
    /// <exception cref="FontFormatException">If the DICT is malformed.</exception>
    public static CffDict Parse(ReadOnlySpan<byte> data, bool cff2, Func<int, int>? regionCount = null)
    {
        int maxStack = cff2 ? 513 : 48;
        var entries = new Dictionary<int, double[]>();
        var stack = new List<double>();
        int vsIndex = 0;
        int i = 0;

        while (i < data.Length)
        {
            byte b0 = data[i];

            if (b0 <= 24)
            {
                int op = b0;
                i++;
                if (b0 == 12)
                {
                    if (i >= data.Length)
                        throw new FontFormatException("CFF DICT ends inside a two-byte operator.");

                    op = 1200 + data[i];
                    i++;
                }

                if (cff2 && op == Blend)
                {
                    ApplyDefaultBlend(stack, regionCount, vsIndex);
                    continue;
                }

                if (cff2 && op == VsIndex && stack.Count > 0)
                    vsIndex = (int)stack[0];

                entries[op] = [.. stack];
                stack.Clear();
                continue;
            }

            if (stack.Count >= maxStack)
                throw new FontFormatException($"CFF DICT operand stack exceeds {maxStack} entries.");

            stack.Add(ReadOperand(data, ref i));
        }

        if (stack.Count > 0)
            throw new FontFormatException("CFF DICT ends with operands but no operator.");

        return new CffDict(entries);
    }

    private static void ApplyDefaultBlend(List<double> stack, Func<int, int>? regionCount, int vsIndex)
    {
        if (regionCount is null)
            throw new FontFormatException("CFF2 DICT uses blend but the font has no variation store.");

        if (stack.Count == 0)
            throw new FontFormatException("CFF2 DICT blend has no operand count.");

        double nValue = stack[^1];
        int k = regionCount(vsIndex);
        if (nValue < 0 || !double.IsInteger(nValue) || nValue * (k + 1) + 1 > stack.Count)
            throw new FontFormatException("CFF2 DICT blend has too few operands.");

        int n = (int)nValue;
        int defaultsStart = stack.Count - 1 - (n * (k + 1));
        stack.RemoveRange(defaultsStart + n, (n * k) + 1);
    }

    private static double ReadOperand(ReadOnlySpan<byte> data, ref int i)
    {
        byte b0 = data[i++];

        if (b0 is >= 32 and <= 246)
            return b0 - 139;

        if (b0 is >= 247 and <= 250)
            return ((b0 - 247) * 256d) + Next(data, ref i) + 108;

        if (b0 is >= 251 and <= 254)
            return -((b0 - 251) * 256d) - Next(data, ref i) - 108;

        switch (b0)
        {
            case 28:
                return (short)((Next(data, ref i) << 8) | Next(data, ref i));
            case 29:
                return (Next(data, ref i) << 24) | (Next(data, ref i) << 16) | (Next(data, ref i) << 8) | Next(data, ref i);
            case 30:
                return ReadReal(data, ref i);
            default:
                throw new FontFormatException($"CFF DICT contains reserved byte {b0}.");
        }
    }

    private static int Next(ReadOnlySpan<byte> data, ref int i)
    {
        if (i >= data.Length)
            throw new FontFormatException("CFF DICT ends inside a number.");

        return data[i++];
    }

    /// <summary>Reads a nibble-encoded real number (TN#5176 Table 5).</summary>
    private static double ReadReal(ReadOnlySpan<byte> data, ref int i)
    {
        var text = new StringBuilder();
        while (true)
        {
            int b = Next(data, ref i);
            for (int shift = 4; shift >= 0; shift -= 4)
            {
                int nibble = (b >> shift) & 0xF;
                switch (nibble)
                {
                    case <= 9: text.Append((char)('0' + nibble)); break;
                    case 0xA: text.Append('.'); break;
                    case 0xB: text.Append('E'); break;
                    case 0xC: text.Append("E-"); break;
                    case 0xE: text.Append('-'); break;
                    case 0xF:
                        return double.TryParse(text.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                               && double.IsFinite(value)
                            ? value
                            : throw new FontFormatException($"CFF DICT has an invalid real number '{text}'.");
                    default:
                        throw new FontFormatException("CFF DICT real number uses a reserved nibble.");
                }
            }

            if (text.Length > 64)
                throw new FontFormatException("CFF DICT real number is too long.");
        }
    }
}
