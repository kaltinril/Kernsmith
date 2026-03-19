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
}
