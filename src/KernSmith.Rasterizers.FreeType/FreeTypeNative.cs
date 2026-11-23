using System.Runtime.InteropServices;
using FreeTypeSharp;

namespace KernSmith.Rasterizers.FreeType;

/// <summary>
/// Custom P/Invoke declarations for FreeType functions not exposed by FreeTypeSharp.
/// </summary>
internal static unsafe class FreeTypeNative
{
    private const string LibName = "freetype";

    /// <summary>
    /// FT_LOAD_COLOR flag (1 &lt;&lt; 20). Requests color glyph layers when available.
    /// </summary>
    public const int FT_LOAD_COLOR = 1 << 20;

    /// <summary>
    /// FT_LOAD_NO_SVG flag (1 &lt;&lt; 24). Ignores a glyph's OT-SVG document. FreeType renders
    /// SVG only through hooks an application installs, and KernSmith installs none, so without
    /// this <see cref="FT_LOAD_COLOR"/> loads such a glyph into a slot that cannot be rendered.
    /// </summary>
    public const int FT_LOAD_NO_SVG = 1 << 24;

    /// <summary>
    /// FT_LOAD_NO_HINTING flag (1 &lt;&lt; 1 = 2). Disables TrueType hinting.
    /// </summary>
    public const int FT_LOAD_NO_HINTING = 1 << 1;

    /// <summary>
    /// FT_LOAD_TARGET_MONO flag. Tells the hinter to optimize for monochrome (1-bit) output.
    /// Value is (FT_RENDER_MODE_MONO &lt;&lt; 16) = (2 &lt;&lt; 16) = 0x20000.
    /// </summary>
    public const int FT_LOAD_TARGET_MONO = 2 << 16;

    /// <summary>
    /// FT_LOAD_TARGET_LIGHT flag: the auto-hinter's light mode, which grid-fits the vertical axis
    /// only and leaves advances linear. A load-time target, not a render mode.
    /// Value is (FT_RENDER_MODE_LIGHT &lt;&lt; 16) = (1 &lt;&lt; 16) = 0x10000. The targets share
    /// one four-bit field, so never combine it with <see cref="FT_LOAD_TARGET_MONO"/>.
    /// </summary>
    public const int FT_LOAD_TARGET_LIGHT = 1 << 16;

    /// <summary>
    /// FT_LOAD_NO_BITMAP flag (1 &lt;&lt; 3 = 8). Do not load embedded bitmaps; load outlines only.
    /// </summary>
    public const int FT_LOAD_NO_BITMAP = 1 << 3;

    // FT_Stroker line cap and line join constants.
    public const int FT_STROKER_LINECAP_ROUND = 1;
    public const int FT_STROKER_LINEJOIN_ROUND = 1;

    /// <summary>FT_ENCODING_UNICODE: FT_ENC_TAG('u', 'n', 'i', 'c').</summary>
    public const uint FT_ENCODING_UNICODE = 0x756E6963;

    /// <summary>FT_ENCODING_MS_SYMBOL: FT_ENC_TAG('s', 'y', 'm', 'b'), the Windows (3,0) cmap.</summary>
    public const uint FT_ENCODING_MS_SYMBOL = 0x73796D62;

    /// <summary>
    /// Makes the face's charmap the one with the given encoding (an FT_Encoding tag); fails with
    /// an error when the face has none.
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Select_Charmap(FT_FaceRec_* face, uint encoding);

    /// <summary>
    /// Selects a bitmap strike by index for bitmap-only fonts.
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Select_Size(FT_FaceRec_* face, int strike_index);

    /// <summary>
    /// Sets the design coordinates for a variable font's variation axes.
    /// Each coordinate is an FT_Fixed value (16.16 fixed-point: multiply float by 65536).
    /// The coords array must contain one entry per axis, ordered as defined in the fvar table.
    /// FT_Fixed is a C long, which the bundled FreeType makes pointer-sized — hence nint, the
    /// same width FreeTypeSharp uses for FT_F26Dot6. A 32-bit element type is read in pairs.
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Set_Var_Design_Coordinates(
        FT_FaceRec_* face,
        uint num_coords,
        nint* coords);

    /// <summary>
    /// Selects a color palette by index for CPAL-based color fonts.
    /// Pass null for <paramref name="palette"/> if you don't need the palette colors back.
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Palette_Select(
        FT_FaceRec_* face,
        ushort palette_index,
        IntPtr palette);

    /// <summary>FT_LayerIterator: the state <see cref="FT_Get_Color_Glyph_Layer"/> walks a glyph's layers with.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FT_LayerIterator
    {
        public uint num_layers;
        public uint layer;
        public byte* p;
    }

    /// <summary>
    /// Reads the next <c>COLR</c> version 0 layer of a base glyph, bottom layer first; false when
    /// there are no more, or the glyph has none. Zero <paramref name="iterator"/> to start.
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool FT_Get_Color_Glyph_Layer(
        FT_FaceRec_* face,
        uint base_glyph,
        out uint glyph_index,
        out uint color_index,
        ref FT_LayerIterator iterator);

    /// <summary>FT_Palette_Data: the face's <c>CPAL</c> description.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FT_Palette_Data
    {
        public ushort num_palettes;
        public ushort* palette_name_ids;
        public ushort* palette_flags;
        public ushort num_palette_entries;
        public ushort* palette_entry_name_ids;
    }

    /// <summary>FT_PALETTE_FOR_DARK_BACKGROUND: the palette is meant for a dark background.</summary>
    public const ushort FT_PALETTE_FOR_DARK_BACKGROUND = 0x02;

    /// <summary>Reads the face's palette description.</summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Palette_Data_Get(FT_FaceRec_* face, FT_Palette_Data* data);

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

    // ---------------------------------------------------------------
    // FT_Outline_Embolden — lightweight synthetic bold
    // ---------------------------------------------------------------

    /// <summary>
    /// Emboldens a glyph outline by moving contour points outward.
    /// <paramref name="strength"/> is in 26.6 fixed-point units (64 = 1 pixel).
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Outline_Embolden(FT_Outline_* outline, int strength);

    /// <summary>An outline's control box in 26.6 fixed point (FT_BBox; FT_Pos is pointer-sized here).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FT_BBox
    {
        public nint xMin, yMin, xMax, yMax;
    }

    /// <summary>Computes the control box of an outline (the box FreeType presets a bitmap from).</summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FT_Outline_Get_CBox(FT_Outline_* outline, FT_BBox* cbox);

    /// <summary>
    /// Sets a driver/module property by name. Used to configure the SDF renderer's
    /// <c>spread</c> (search radius) on the built-in "sdf" module.
    /// The value pointer's type depends on the property; for "sdf"/"spread" it is an
    /// <c>FT_UInt</c> (32-bit unsigned).
    /// </summary>
    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl)]
    public static extern FT_Error FT_Property_Set(
        IntPtr library,
        [MarshalAs(UnmanagedType.LPStr)] string module_name,
        [MarshalAs(UnmanagedType.LPStr)] string property_name,
        void* value);

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

        // FT_Bitmap layout: rows(4) + width(4) + pitch(4) + buffer(ptr, aligned)
        // buffer offset = 8 + roundup(4, ptrSize) = 8 + 8 on 64bit = 16, or 8 + 4 on 32bit = 12
        if (ptrSize == 8)
            buffer = Marshal.ReadIntPtr(bitmapGlyph, bitmapOffset + 16); // 4+4+4+pad4 = 16
        else
            buffer = Marshal.ReadIntPtr(bitmapGlyph, bitmapOffset + 12); // 4+4+4 = 12
    }
}
