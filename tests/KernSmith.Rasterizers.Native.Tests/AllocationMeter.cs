namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Measures what a piece of work allocates on the current thread.</summary>
/// <remarks>
/// <para>
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> is not exact across a garbage collection:
/// when one runs while this thread is inside the window (in a parallel test run, usually a
/// background gen2 collection other threads triggered), the collector can retire this thread's
/// allocation context, and the unused rest of it — anything up to the ~8 KB allocation quantum —
/// is counted as allocated although nothing on this thread allocated it. With
/// <c>DOTNET_gcConcurrent=0</c> the effect disappears, and every inflated window has a GC pause
/// inside it; <see cref="AllocationMeterTests"/> reproduces it on .NET 8 and 10.
/// So a window a collection paused is discarded and the work measured again.
/// </para>
/// <para>
/// A gen2 collection also trims <see cref="System.Buffers.ArrayPool{T}.Shared"/> — under memory
/// pressure, every thread-local slot — from the finalizer thread, after the pause and so with no
/// pause inside the window that follows. The first run after that pays for each pooled buffer
/// the work rents (whole bucket sizes: a 32 KB inflate buffer reads as 32,792 B) and returns them,
/// so the next run is warm again. Hence the smaller of two clean windows is reported: a refill
/// can inflate only one of them, while the work's own allocations are in both — the result is
/// still the thread's exact count, so a real allocation is never hidden.
/// </para>
/// </remarks>
internal static class AllocationMeter
{
    /// <summary>Windows to try before settling for the smallest paused one.</summary>
    private const int Attempts = 20;

    /// <summary>Clean windows to take; the smaller is the answer.</summary>
    private const int CleanWindows = 2;

    /// <summary>
    /// The bytes the current thread allocates running <paramref name="work"/>: the smaller of
    /// two windows no garbage collection paused. <paramref name="work"/> runs several times, so
    /// it must allocate the same on every run (warm it up first).
    /// </summary>
    public static long BytesAllocatedBy(Action work)
    {
        long fewest = long.MaxValue, fewestClean = long.MaxValue;
        int clean = 0;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var paused = GC.GetTotalPauseDuration();
            long before = GC.GetAllocatedBytesForCurrentThread();
            work();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // A collection ran inside the window: it can inflate the count, never shrink it.
            fewest = Math.Min(fewest, allocated);
            if (GC.GetTotalPauseDuration() != paused)
                continue;

            fewestClean = Math.Min(fewestClean, allocated);
            if (++clean == CleanWindows)
                return fewestClean;
        }

        return clean > 0 ? fewestClean : fewest;
    }
}
