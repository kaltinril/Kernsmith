using System.Runtime.InteropServices;
using FreeTypeSharp;

namespace Bmfontier.Rasterizer;

/// <summary>
/// Custom P/Invoke declarations for FreeType functions not exposed by FreeTypeSharp.
/// </summary>
internal static unsafe class FreeTypeNative
{
    private const string LibName = "freetype";

    /// <summary>
    /// FT_LOAD_COLOR flag (1 &lt;&lt; 5 = 32). Requests color glyph layers when available.
    /// Defined here because FreeTypeSharp may not expose it.
    /// </summary>
    public const int FT_LOAD_COLOR = 1 << 5;

    /// <summary>
    /// FT_LOAD_NO_HINTING flag (1 &lt;&lt; 1 = 2). Disables TrueType hinting.
    /// </summary>
    public const int FT_LOAD_NO_HINTING = 1 << 1;

    /// <summary>
    /// FT_LOAD_NO_BITMAP flag (1 &lt;&lt; 3 = 8). Do not load embedded bitmaps; load outlines only.
    /// </summary>
    public const int FT_LOAD_NO_BITMAP = 1 << 3;

    // FT_Stroker line cap and line join constants.
    public const int FT_STROKER_LINECAP_ROUND = 1;
    public const int FT_STROKER_LINEJOIN_ROUND = 1;

    /// <summary>
    /// Sets the design coordinates for a variable font's variation axes.
    /// Each coordinate is an FT_Fixed value (16.16 fixed-point: multiply float by 65536).
    /// The coords array must contain one entry per axis, ordered as defined in the fvar table.
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Set_Var_Design_Coordinates(
        FT_FaceRec_* face,
        uint num_coords,
        int* coords);

    /// <summary>
    /// Selects a color palette by index for CPAL-based color fonts.
    /// Pass null for <paramref name="palette"/> if you don't need the palette colors back.
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Palette_Select(
        FT_FaceRec_* face,
        ushort palette_index,
        IntPtr palette);

    // ---------------------------------------------------------------
    // FT_Stroker API — vector-based outline stroking
    // ---------------------------------------------------------------

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Stroker_New(IntPtr library, out IntPtr stroker);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FT_Stroker_Set(IntPtr stroker, int radius, int lineCap, int lineJoin, int miterLimit);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FT_Stroker_Done(IntPtr stroker);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Get_Glyph(FT_GlyphSlotRec_* slot, out IntPtr glyph);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Glyph_StrokeBorder(ref IntPtr glyph, IntPtr stroker, [MarshalAs(UnmanagedType.U1)] bool inside, [MarshalAs(UnmanagedType.U1)] bool destroy);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Glyph_To_Bitmap(ref IntPtr glyph, int renderMode, IntPtr origin, [MarshalAs(UnmanagedType.U1)] bool destroy);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FT_Done_Glyph(IntPtr glyph);

    /// <summary>
    /// Reads the FT_BitmapGlyphRec_ fields from a glyph pointer after FT_Glyph_To_Bitmap.
    /// Uses manual pointer arithmetic to avoid struct layout portability issues.
    /// </summary>
    /// <remarks>
    /// FT_BitmapGlyphRec_ layout (64-bit):
    ///   FT_GlyphRec_ root:  library(8) + clazz(8) + format(4+pad4) + advance.x(8) + advance.y(8) = 40 bytes
    ///   int left:   4 bytes at offset 40
    ///   int top:    4 bytes at offset 44
    ///   FT_Bitmap:  starts at offset 48
    ///     uint rows(4), uint width(4), int pitch(4), byte* buffer(8), ...
    /// </remarks>
    public static void ReadBitmapGlyph(IntPtr bitmapGlyph,
        out int left, out int top,
        out int rows, out int width, out int pitch, out IntPtr buffer)
    {
        // Offset to left/top after FT_GlyphRec_:
        // library(ptr) + clazz(ptr) + format(int, padded to ptr) + advance(2*long on FT_Pos)
        // On 64-bit: 8 + 8 + 8 (4 + 4 pad) + 8 + 8 = 40
        // On 32-bit: 4 + 4 + 4 + 4 + 4 = 20
        var ptrSize = IntPtr.Size;
        var glyphRecSize = ptrSize      // library
                         + ptrSize      // clazz
                         + ptrSize      // format (padded to pointer alignment)
                         + ptrSize      // advance.x (FT_Pos)
                         + ptrSize;     // advance.y (FT_Pos)

        left = Marshal.ReadInt32(bitmapGlyph, glyphRecSize);
        top = Marshal.ReadInt32(bitmapGlyph, glyphRecSize + 4);

        // FT_Bitmap starts after left(4) + top(4) = +8, but may need alignment padding.
        // On 64-bit, left(4) + top(4) = 8 bytes, no padding needed before FT_Bitmap.
        var bitmapOffset = glyphRecSize + 8;

        rows = Marshal.ReadInt32(bitmapGlyph, bitmapOffset);        // unsigned int rows
        width = Marshal.ReadInt32(bitmapGlyph, bitmapOffset + 4);   // unsigned int width
        pitch = Marshal.ReadInt32(bitmapGlyph, bitmapOffset + 8);   // int pitch
        buffer = Marshal.ReadIntPtr(bitmapGlyph, bitmapOffset + 8 + ptrSize); // byte* buffer (after pitch, padded)

        // On 64-bit: pitch is at +12 within FT_Bitmap, buffer at +16 (pitch(4) + 4pad + ptr)
        // Actually FT_Bitmap layout: rows(4) + width(4) + pitch(4) + buffer(ptr, aligned)
        // So buffer offset = 8 + roundup(4, ptrSize) = 8 + 8 on 64bit = 16, or 8 + 4 on 32bit = 12
        if (ptrSize == 8)
            buffer = Marshal.ReadIntPtr(bitmapGlyph, bitmapOffset + 16); // 4+4+4+pad4 = 16
        else
            buffer = Marshal.ReadIntPtr(bitmapGlyph, bitmapOffset + 12); // 4+4+4 = 12
    }
}
