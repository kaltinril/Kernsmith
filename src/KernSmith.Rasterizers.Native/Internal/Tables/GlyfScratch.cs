using KernSmith.Rasterizers.Native.Internal.Outlines;

namespace KernSmith.Rasterizers.Native.Internal.Tables;

/// <summary>
/// Reusable buffers for <see cref="GlyfTable.DecodeSimpleOutline"/>: point flags, coordinates,
/// one materialised contour and the command list, grown to the largest glyph seen and then
/// kept, so decoding a glyph allocates nothing but its outline.
/// </summary>
/// <remarks>
/// Not thread-safe: one instance per decoder, and a decoder belongs to one
/// <see cref="NativeRasterizer"/>, which is single-threaded (Phase 160, D9) — core creates a
/// rasterizer per generation and never shares one across threads. Every span handed out is
/// valid only until the next call for the same buffer.
/// </remarks>
internal sealed class GlyfScratch
{
    private int[] _endPoints = [];
    private byte[] _flags = [];
    private int[] _xs = [];
    private int[] _ys = [];
    private GlyphPoint[] _points = [];
    private OutlineCommand[] _commands = [];

    /// <summary>Room for <paramref name="count"/> contour end points.</summary>
    public Span<int> EndPoints(int count) => Ensure(ref _endPoints, count);

    /// <summary>Room for <paramref name="count"/> point flags.</summary>
    public Span<byte> Flags(int count) => Ensure(ref _flags, count);

    /// <summary>Room for <paramref name="count"/> X coordinates.</summary>
    public Span<int> Xs(int count) => Ensure(ref _xs, count);

    /// <summary>Room for <paramref name="count"/> Y coordinates.</summary>
    public Span<int> Ys(int count) => Ensure(ref _ys, count);

    /// <summary>Room for one contour of up to <paramref name="count"/> points.</summary>
    public Span<GlyphPoint> Points(int count) => Ensure(ref _points, count);

    /// <summary>Room for up to <paramref name="count"/> outline commands.</summary>
    public Span<OutlineCommand> Commands(int count) => Ensure(ref _commands, count);

    private static Span<T> Ensure<T>(ref T[] buffer, int count)
    {
        // Contents need not survive growth: every caller overwrites what it reads.
        if (buffer.Length < count)
            buffer = new T[Math.Max(count, buffer.Length * 2)];
        return buffer.AsSpan(0, count);
    }
}
