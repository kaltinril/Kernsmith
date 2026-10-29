namespace KernSmith.Rasterizer;

/// <summary>
/// A backend-drawn outline layer (<see cref="IRasterizer.RasterizeOutlineLayer"/>) as one byte
/// of coverage per pixel, placed relative to its body glyph's bitmap: pixel (0, 0) of the mask
/// sits at (<see cref="OffsetX"/>, <see cref="OffsetY"/>) from the body's top-left pixel. Every
/// transform the pipeline applies to the body after rasterizing is applied here in that same
/// frame, so the layer stays on the body to the pixel whatever rounding the body's own metrics
/// go through.
/// </summary>
/// <remarks>Empty rows and columns are always trimmed, so the mask's bounds are its ink and a
/// layer never grows a cell further than its coverage reaches.</remarks>
internal sealed class OutlineMask
{
    private OutlineMask(byte[] alpha, int width, int height, int offsetX, int offsetY)
    {
        Alpha = alpha;
        Width = width;
        Height = height;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    /// <summary>Coverage, <see cref="Width"/> bytes per row.</summary>
    public byte[] Alpha { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Column of the body's bitmap that mask column 0 lies on (negative: left of it).</summary>
    public int OffsetX { get; }

    /// <summary>Row of the body's bitmap that mask row 0 lies on (negative: above it).</summary>
    public int OffsetY { get; }

    /// <summary>
    /// Places a backend layer against the glyph it was drawn for, both positioned by their
    /// pen-relative metrics. Null when the layer has no coverage.
    /// </summary>
    public static OutlineMask? FromLayer(RasterizedGlyph body, RasterizedGlyph layer)
    {
        int w = layer.Width, h = layer.Height;
        if (w <= 0 || h <= 0 || layer.BitmapData.Length == 0)
            return null;

        var alpha = new byte[w * h];
        int bpp = layer.Format == PixelFormat.Rgba32 ? 4 : 1;
        int channel = bpp == 4 ? 3 : 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int index = (y * layer.Pitch) + (x * bpp) + channel;
                alpha[(y * w) + x] = index < layer.BitmapData.Length ? layer.BitmapData[index] : (byte)0;
            }
        }

        return Trim(alpha, w, h,
            layer.Metrics.BearingX - body.Metrics.BearingX,
            body.Metrics.BearingY - layer.Metrics.BearingY);
    }

    /// <summary>The mask after its body has been moved by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// within a larger canvas: the body's new top-left is at (dx, dy) in its old frame.</summary>
    public OutlineMask Shift(int dx, int dy) =>
        dx == 0 && dy == 0 ? this : new OutlineMask(Alpha, Width, Height, OffsetX - dx, OffsetY - dy);

    /// <summary>
    /// The mask after its body went through <see cref="HeightStretchPostProcessor"/>: each body
    /// row r of the stretched bitmap samples unstretched row <c>r · before / after</c>, so the mask
    /// is resampled with that same mapping, rows above and below the body included.
    /// <paramref name="threshold"/> keeps an aliased (<see cref="AntiAliasMode.None"/>) layer hard.
    /// </summary>
    public OutlineMask? Stretch(int bodyHeightBefore, int bodyHeightAfter, bool threshold)
    {
        if (bodyHeightBefore <= 0 || bodyHeightAfter <= 0 || bodyHeightBefore == bodyHeightAfter)
            return this;

        double yScale = (double)bodyHeightBefore / bodyHeightAfter;
        int first = (int)Math.Floor(OffsetY / yScale) - 1;
        int last = (int)Math.Ceiling((OffsetY + Height) / yScale) + 1;
        int h = last - first;
        var alpha = new byte[Width * h];

        for (int row = 0; row < h; row++)
        {
            double source = ((first + row) * yScale) - OffsetY;
            int r0 = (int)Math.Floor(source);
            float f = (float)(source - r0);
            for (int x = 0; x < Width; x++)
            {
                int v0 = At(x, r0), v1 = At(x, r0 + 1);
                var v = (byte)(v0 + ((v1 - v0) * f));
                alpha[(row * Width) + x] = threshold ? (v >= 128 ? (byte)255 : (byte)0) : v;
            }
        }

        return Trim(alpha, Width, h, OffsetX, first);
    }

    /// <summary>
    /// The mask after its body was box-filtered down by <paramref name="level"/>: output pixel
    /// (c, r) of the body averages body pixels [c·level, (c+1)·level) × [r·level, (r+1)·level), so
    /// the mask averages the same blocks of its own frame.
    /// </summary>
    public OutlineMask? Downscale(int level)
    {
        if (level <= 1)
            return this;

        int c0 = FloorDiv(OffsetX, level), c1 = FloorDiv(OffsetX + Width - 1, level) + 1;
        int r0 = FloorDiv(OffsetY, level), r1 = FloorDiv(OffsetY + Height - 1, level) + 1;
        int w = c1 - c0, h = r1 - r0, area = level * level;
        var alpha = new byte[w * h];
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int sum = 0;
                for (int sy = 0; sy < level; sy++)
                {
                    for (int sx = 0; sx < level; sx++)
                        sum += At(((c0 + c) * level) + sx - OffsetX, ((r0 + r) * level) + sy - OffsetY);
                }

                alpha[(r * w) + c] = (byte)(sum / area);
            }
        }

        return Trim(alpha, w, h, c0, r0);
    }

    /// <summary>
    /// The outline's coverage over exactly the canvas the effect uses — the body grown by
    /// <paramref name="outlineWidth"/> on every side — with full coverage under every pixel of the
    /// body (as the effect backs the body, so the body composites over it without a seam).
    /// </summary>
    /// <remarks>
    /// INVARIANT: the canvas never depends on the mask, so switching between
    /// <see cref="OutlineMode.Effect"/> and <see cref="OutlineMode.Vector"/> never changes a cell,
    /// bearing or <c>.fnt</c> offset. A layer reaches no further than the width itself; what a
    /// mask can carry past it is resampling fringe (a height stretch's interpolated edge row, a
    /// box filter's partial block), which is clipped.
    /// </remarks>
    /// <returns>Coverage, the canvas size, and the canvas origin relative to the body's top-left.</returns>
    public (byte[] Alpha, int Width, int Height, int OffsetX, int OffsetY) Canvas(
        byte[] bodyAlpha, int bodyWidth, int bodyHeight, int bodyPitch, int outlineWidth)
    {
        int minX = -outlineWidth, minY = -outlineWidth;
        int w = bodyWidth + (2 * outlineWidth), h = bodyHeight + (2 * outlineWidth);
        var alpha = new byte[w * h];

        int x0 = Math.Max(OffsetX, minX), x1 = Math.Min(OffsetX + Width, minX + w);
        int y0 = Math.Max(OffsetY, minY), y1 = Math.Min(OffsetY + Height, minY + h);
        for (int y = y0; y < y1 && x0 < x1; y++)
        {
            Array.Copy(Alpha, ((y - OffsetY) * Width) + x0 - OffsetX, alpha, ((y - minY) * w) + x0 - minX, x1 - x0);
        }

        for (int y = 0; y < bodyHeight; y++)
        {
            for (int x = 0; x < bodyWidth; x++)
            {
                int index = (y * bodyPitch) + x;
                if (index < bodyAlpha.Length && bodyAlpha[index] > 0)
                    alpha[((y - minY) * w) + x - minX] = 255;
            }
        }

        return (alpha, w, h, minX, minY);
    }

    private int At(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? 0 : Alpha[(y * Width) + x];

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>Crops empty rows and columns; null when nothing is left.</summary>
    private static OutlineMask? Trim(byte[] alpha, int width, int height, int offsetX, int offsetY)
    {
        int left = width, right = -1, top = height, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (alpha[(y * width) + x] == 0)
                    continue;

                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < 0)
            return null;

        if (left == 0 && top == 0 && right == width - 1 && bottom == height - 1)
            return new OutlineMask(alpha, width, height, offsetX, offsetY);

        int w = right - left + 1, h = bottom - top + 1;
        var cropped = new byte[w * h];
        for (int y = 0; y < h; y++)
            Array.Copy(alpha, ((top + y) * width) + left, cropped, y * w, w);

        return new OutlineMask(cropped, w, h, offsetX + left, offsetY + top);
    }
}
