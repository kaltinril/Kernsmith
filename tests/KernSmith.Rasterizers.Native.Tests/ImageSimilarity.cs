namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Image comparison metrics shared by the rasterizer comparison tests.</summary>
internal static class ImageSimilarity
{
    /// <summary>
    /// Mean structural similarity over 8x8 windows, the usual formulation with C1 = (0.01*255)^2
    /// and C2 = (0.03*255)^2. Images smaller than one window are scored as a single window.
    /// </summary>
    public static double Ssim(byte[] a, byte[] b, int width, int height)
    {
        const double C1 = 6.5025;    // (0.01 * 255)^2
        const double C2 = 58.5225;   // (0.03 * 255)^2
        int window = 8;

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
                {
                    for (int x = wx; x < x1; x++)
                    {
                        meanA += a[(y * width) + x];
                        meanB += b[(y * width) + x];
                    }
                }

                meanA /= n;
                meanB /= n;

                double varA = 0, varB = 0, covariance = 0;
                for (int y = wy; y < y1; y++)
                {
                    for (int x = wx; x < x1; x++)
                    {
                        double da = a[(y * width) + x] - meanA;
                        double db = b[(y * width) + x] - meanB;
                        varA += da * da;
                        varB += db * db;
                        covariance += da * db;
                    }
                }

                double divisor = Math.Max(1, n - 1);
                varA /= divisor;
                varB /= divisor;
                covariance /= divisor;

                total += (((2 * meanA * meanB) + C1) * ((2 * covariance) + C2))
                    / (((meanA * meanA) + (meanB * meanB) + C1) * (varA + varB + C2));
                windows++;
            }
        }

        return windows == 0 ? 1.0 : total / windows;
    }
}
