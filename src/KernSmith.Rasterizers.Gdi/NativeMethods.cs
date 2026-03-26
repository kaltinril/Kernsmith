using System.Runtime.InteropServices;

namespace KernSmith.Rasterizers.Gdi;

/// <summary>
/// Win32 GDI P/Invoke declarations for font rasterization.
/// </summary>
internal static partial class NativeMethods
{
    // ── Constants ────────────────────────────────────────────────────

    internal const uint GGO_GRAY8_BITMAP = 6;
    internal const uint GGO_METRICS = 0;
    internal const int MM_TEXT = 1;
    internal const int FW_NORMAL = 400;
    internal const int FW_BOLD = 700;
    internal const byte DEFAULT_CHARSET = 1;
    internal const byte OUT_DEFAULT_PRECIS = 0;
    internal const byte OUT_TT_PRECIS = 4;
    internal const byte OUT_TT_ONLY_PRECIS = 7;
    internal const byte CLIP_DEFAULT_PRECIS = 0;
    internal const byte ANTIALIASED_QUALITY = 4;
    internal const byte NONANTIALIASED_QUALITY = 3;
    internal const byte DEFAULT_PITCH = 0;
    internal const byte FF_DONTCARE = 0;
    internal const uint GDI_ERROR = 0xFFFFFFFF;

    // ── gdi32.dll functions ─────────────────────────────────────────

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFontIndirectW(ref LOGFONTW lplf);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetGlyphOutlineW(
        IntPtr hdc,
        uint uChar,
        uint fuFormat,
        out GLYPHMETRICS lpgm,
        uint cjBuffer,
        IntPtr pvBuffer,
        ref MAT2 lpmat2);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTextMetricsW(IntPtr hdc, out TEXTMETRICW lptm);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCharABCWidthsW(IntPtr hdc, uint wFirst, uint wLast, out ABC lpabc);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern int SetMapMode(IntPtr hdc, int iMode);

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern IntPtr AddFontMemResourceEx(
        IntPtr pFileView,
        uint cjSize,
        IntPtr pvReserved,
        ref uint pNumFonts);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveFontMemResourceEx(IntPtr h);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetGlyphIndicesW(
        IntPtr hdc,
        [MarshalAs(UnmanagedType.LPWStr)] string lpstr,
        int c,
        [Out] ushort[] pgi,
        uint fl);

    internal const uint GGI_MARK_NONEXISTING_GLYPHS = 0x0001;

    [DllImport("gdi32.dll", SetLastError = true)]
    internal static extern uint GetKerningPairsW(IntPtr hdc, uint nNumPairs, [Out] KERNINGPAIR[]? lpkrnpair);

    // ── Structs ─────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct KERNINGPAIR
    {
        public ushort WFirst;
        public ushort WSecond;
        public int IKernAmount;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FIXED
    {
        public ushort Fract;
        public short Value;

        public FIXED(short value, ushort fract)
        {
            Value = value;
            Fract = fract;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GLYPHMETRICS
    {
        public uint GmBlackBoxX;
        public uint GmBlackBoxY;
        public POINT GmptGlyphOrigin;
        public short GmCellIncX;
        public short GmCellIncY;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MAT2
    {
        public FIXED EM11;
        public FIXED EM12;
        public FIXED EM21;
        public FIXED EM22;

        /// <summary>Returns the 2x2 identity matrix.</summary>
        public static MAT2 Identity => new()
        {
            EM11 = new FIXED(1, 0),
            EM12 = new FIXED(0, 0),
            EM21 = new FIXED(0, 0),
            EM22 = new FIXED(1, 0)
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ABC
    {
        public int AbcA;
        public uint AbcB;
        public int AbcC;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct LOGFONTW
    {
        public int LfHeight;
        public int LfWidth;
        public int LfEscapement;
        public int LfOrientation;
        public int LfWeight;
        public byte LfItalic;
        public byte LfUnderline;
        public byte LfStrikeOut;
        public byte LfCharSet;
        public byte LfOutPrecision;
        public byte LfClipPrecision;
        public byte LfQuality;
        public byte LfPitchAndFamily;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? LfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TEXTMETRICW
    {
        public int TmHeight;
        public int TmAscent;
        public int TmDescent;
        public int TmInternalLeading;
        public int TmExternalLeading;
        public int TmAveCharWidth;
        public int TmMaxCharWidth;
        public int TmWeight;
        public int TmOverhang;
        public int TmDigitizedAspectX;
        public int TmDigitizedAspectY;
        public char TmFirstChar;
        public char TmLastChar;
        public char TmDefaultChar;
        public char TmBreakChar;
        public byte TmItalic;
        public byte TmUnderlined;
        public byte TmStruckOut;
        public byte TmPitchAndFamily;
        public byte TmCharSet;
    }
}
