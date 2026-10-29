namespace KernSmith.Imaging.Internal;

/// <summary>
/// The reduced images an image is stored as: one for a non-interlaced image, or Adam7's seven
/// passes, each a sub-grid with its own origin and step.
/// </summary>
internal static class Adam7
{
    /// <summary>One reduced image: pixels at (StartX + i·StepX, StartY + j·StepY).</summary>
    public readonly record struct Pass(int StartX, int StartY, int StepX, int StepY);

    // The 8x8 Adam7 pattern of the spec, as per-pass origins and steps.
    private static readonly Pass[] Interlaced =
    [
        new(0, 0, 8, 8),
        new(4, 0, 8, 8),
        new(0, 4, 4, 8),
        new(2, 0, 4, 4),
        new(0, 2, 2, 4),
        new(1, 0, 2, 2),
        new(0, 1, 1, 2),
    ];

    private static readonly Pass[] Single = [new(0, 0, 1, 1)];

    public static ReadOnlySpan<Pass> Passes(bool interlaced) => interlaced ? Interlaced : Single;

    /// <summary>Columns in a pass; 0 when the image is too narrow for it.</summary>
    public static int Width(Pass pass, int imageWidth) =>
        imageWidth <= pass.StartX ? 0 : (int)(((long)imageWidth - pass.StartX + pass.StepX - 1) / pass.StepX);

    /// <summary>Rows in a pass; 0 when the image is too short for it.</summary>
    public static int Height(Pass pass, int imageHeight) =>
        imageHeight <= pass.StartY ? 0 : (int)(((long)imageHeight - pass.StartY + pass.StepY - 1) / pass.StepY);

    /// <summary>Bytes in one filtered row of <paramref name="columns"/> pixels, excluding the filter-type byte.</summary>
    public static long RowBytes(long columns, int bitsPerPixel) => ((columns * bitsPerPixel) + 7) / 8;

    /// <summary>
    /// The decompressed size the header implies: every non-empty pass's rows, each a filter-type
    /// byte plus its pixels. An empty pass contributes nothing, not even filter bytes. Saturates at
    /// <see cref="long.MaxValue"/> for a size no array could hold.
    /// </summary>
    public static long RawSize(PngHeader header)
    {
        int bitsPerPixel = new PixelFormat(header).BitsPerPixel;
        long total = 0;
        foreach (var pass in Passes(header.Interlaced))
        {
            int width = Width(pass, header.Width);
            int height = Height(pass, header.Height);
            if (width == 0 || height == 0)
                continue;
            long rowLength = 1 + RowBytes(width, bitsPerPixel);
            if (rowLength > Array.MaxLength / height)
                return long.MaxValue;
            total += height * rowLength;
        }

        return total;
    }
}
