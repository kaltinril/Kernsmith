namespace KernSmith.HintParity;

/// <summary>Two glyph bitmaps placed on one canvas by their bearings, ready to compare pixel for pixel.</summary>
internal sealed class CommonCanvas
{
    /// <summary>Background around the union box, so SSIM windows see the glyph's edge against empty space.</summary>
    public const int Pad = 2;

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] A { get; init; }
    public required byte[] B { get; init; }

    public static CommonCanvas Place(GlyphBitmap a, GlyphBitmap b)
    {
        int left = Math.Min(a.XOffset, b.XOffset);
        int top = Math.Min(a.BaselineTop, b.BaselineTop);
        int right = Math.Max(a.XOffset + a.Width, b.XOffset + b.Width);
        int bottom = Math.Max(a.BaselineTop + a.Height, b.BaselineTop + b.Height);
        int width = right - left + (2 * Pad);
        int height = bottom - top + (2 * Pad);

        return new CommonCanvas
        {
            Width = width,
            Height = height,
            A = Paste(a, left, top, width, height),
            B = Paste(b, left, top, width, height),
        };
    }

    private static byte[] Paste(GlyphBitmap g, int left, int top, int width, int height)
    {
        var canvas = new byte[width * height];
        int ox = g.XOffset - left + Pad;
        int oy = g.BaselineTop - top + Pad;
        for (int y = 0; y < g.Height; y++)
            Array.Copy(g.Pixels, y * g.Width, canvas, ((oy + y) * width) + ox, g.Width);
        return canvas;
    }
}

/// <summary>Per-glyph comparison of FreeType (A) against Native (B).</summary>
internal sealed record GlyphComparison(
    int Id,
    int AdvanceA, int AdvanceB,
    GlyphBitmap A, GlyphBitmap B,
    bool BoxIdentical, bool BoxWithin1,
    bool BothEmpty,
    bool PixelIdentical,
    double MeanAbsDiff, int MaxAbsDiff,
    double Ssim,
    int XorPixels)
{
    public bool AdvanceMatch => AdvanceA == AdvanceB;

    public static GlyphComparison Compare(GlyphBitmap a, GlyphBitmap b, bool mono)
    {
        int dw = Math.Abs(a.Width - b.Width);
        int dh = Math.Abs(a.Height - b.Height);
        int dx = Math.Abs(a.XOffset - b.XOffset);
        int dy = Math.Abs(a.BaselineTop - b.BaselineTop);
        bool boxIdentical = dw == 0 && dh == 0 && dx == 0 && dy == 0;
        bool boxWithin1 = dw <= 1 && dh <= 1 && dx <= 1 && dy <= 1;

        if (a.IsEmpty && b.IsEmpty)
            return new GlyphComparison(a.Id, a.XAdvance, b.XAdvance, a, b, boxIdentical, boxWithin1, true, true, 0, 0, 1.0, 0);

        var canvas = CommonCanvas.Place(a, b);
        long sum = 0;
        int max = 0;
        int xor = 0;
        for (int i = 0; i < canvas.A.Length; i++)
        {
            int d = Math.Abs(canvas.A[i] - canvas.B[i]);
            sum += d;
            if (d > max) max = d;
            if ((canvas.A[i] >= 128) != (canvas.B[i] >= 128)) xor++;
        }

        double ssim = mono ? (xor == 0 ? 1.0 : ComputeSsim(canvas.A, canvas.B, canvas.Width, canvas.Height))
                           : ComputeSsim(canvas.A, canvas.B, canvas.Width, canvas.Height);
        return new GlyphComparison(
            a.Id, a.XAdvance, b.XAdvance, a, b, boxIdentical, boxWithin1, false,
            PixelIdentical: max == 0,
            MeanAbsDiff: (double)sum / canvas.A.Length,
            MaxAbsDiff: max,
            Ssim: ssim,
            XorPixels: xor);
    }

    /// <summary>
    /// Mean structural similarity over 8x8 windows (C1 = (0.01*255)^2, C2 = (0.03*255)^2), the same
    /// formulation as the Native test project's <c>ImageSimilarity.Ssim</c>; partial windows at the
    /// right and bottom edges count as windows of their own.
    /// </summary>
    public static double ComputeSsim(byte[] a, byte[] b, int width, int height)
    {
        const double C1 = 6.5025;
        const double C2 = 58.5225;
        const int window = 8;

        double total = 0;
        int windows = 0;

        for (int wy = 0; wy < height; wy += window)
        {
            for (int wx = 0; wx < width; wx += window)
            {
                int x1 = Math.Min(wx + window, width);
                int y1 = Math.Min(wy + window, height);
                int n = (x1 - wx) * (y1 - wy);
                if (n == 0)
                    continue;

                double meanA = 0, meanB = 0;
                for (int y = wy; y < y1; y++)
                    for (int x = wx; x < x1; x++)
                    {
                        meanA += a[(y * width) + x];
                        meanB += b[(y * width) + x];
                    }

                meanA /= n;
                meanB /= n;

                double varA = 0, varB = 0, cov = 0;
                for (int y = wy; y < y1; y++)
                    for (int x = wx; x < x1; x++)
                    {
                        double da = a[(y * width) + x] - meanA;
                        double db = b[(y * width) + x] - meanB;
                        varA += da * da;
                        varB += db * db;
                        cov += da * db;
                    }

                double divisor = Math.Max(1, n - 1);
                varA /= divisor;
                varB /= divisor;
                cov /= divisor;

                total += (((2 * meanA * meanB) + C1) * ((2 * cov) + C2))
                    / (((meanA * meanA) + (meanB * meanB) + C1) * (varA + varB + C2));
                windows++;
            }
        }

        return windows == 0 ? 1.0 : total / windows;
    }
}
