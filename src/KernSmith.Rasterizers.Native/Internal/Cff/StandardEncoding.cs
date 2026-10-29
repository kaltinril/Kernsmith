namespace KernSmith.Rasterizers.Native.Internal.Cff;

/// <summary>
/// The CFF predefined Standard Encoding (TN#5176 Appendix B) as character code → SID. Only
/// <c>seac</c> needs it: its base and accent operands are Standard Encoding codes.
/// </summary>
internal static class StandardEncoding
{
    /// <summary>Codes 161–255 that are mapped, paired with their SIDs (96–149 in order).</summary>
    private static readonly byte[] UpperCodes =
    [
        161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175,
        177, 178, 179, 180, 182, 183, 184, 185, 186, 187, 188, 189, 191,
        193, 194, 195, 196, 197, 198, 199, 200, 202, 203, 205, 206, 207, 208,
        225, 227, 232, 233, 234, 235, 241, 245, 248, 249, 250, 251,
    ];

    /// <summary>The SID for a Standard Encoding code, or 0 (.notdef) when the code is unmapped.</summary>
    public static int Sid(int code)
    {
        // Codes 32–126 map to SIDs 1–95 in order.
        if (code is >= 32 and <= 126)
            return code - 31;

        int upper = Array.IndexOf(UpperCodes, (byte)Math.Clamp(code, 0, 255));
        return code is >= 161 and <= 255 && upper >= 0 ? 96 + upper : 0;
    }
}
