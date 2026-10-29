namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>Measures what a piece of work allocates on the current thread.</summary>
/// <remarks>
/// <see cref="GC.GetAllocatedBytesForCurrentThread"/> is not exact across a garbage collection:
/// when one runs while this thread is inside the window (in a parallel test run, usually a
/// background gen2 collection other threads triggered), the collector can retire this thread's
/// allocation context, and the unused rest of it — anything up to the ~8 KB allocation quantum —
/// is counted as allocated although nothing on this thread allocated it. With
/// <c>DOTNET_gcConcurrent=0</c> the effect disappears, and every inflated window has a GC pause
/// inside it; <see cref="AllocationMeterTests"/> reproduces it on .NET 8 and 10.
/// So a window a collection paused is discarded and the work measured again. The result is still
/// the thread's own exact count, so a real allocation is never hidden.
/// </remarks>
internal static class AllocationMeter
{
    /// <summary>Windows to try before settling for the smallest paused one.</summary>
    private const int Attempts = 20;

    /// <summary>
    /// The bytes the current thread allocates running <paramref name="work"/>, measured in a
    /// window no garbage collection paused. <paramref name="work"/> may run several times, so it
    /// must allocate the same on every run (warm it up first).
    /// </summary>
    public static long BytesAllocatedBy(Action work)
    {
        long fewest = long.MaxValue;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var paused = GC.GetTotalPauseDuration();
            long before = GC.GetAllocatedBytesForCurrentThread();
            work();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (GC.GetTotalPauseDuration() == paused)
                return allocated;

            // A collection ran inside the window: it can inflate the count, never shrink it.
            fewest = Math.Min(fewest, allocated);
        }

        return fewest;
    }
}
