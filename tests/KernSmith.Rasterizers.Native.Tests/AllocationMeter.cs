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
/// so the next run is warm again. Each element type's pool trims from its own callback, so work
/// that rents from two (the colour-bitmap path: bytes to decode, floats to resample) can pay one
/// refill in one window and the other in the next.
/// </para>
/// <para>
/// All of this only ever adds to a window, and never the same amount for long, while the work's
/// own allocations are in every window. So the answer is the smallest window, taken once
/// <see cref="Confirmations"/> clean windows have measured exactly that: still the thread's exact
/// count, so a real allocation is never hidden.
/// </para>
/// </remarks>
internal static class AllocationMeter
{
    /// <summary>Windows to try before settling for the smallest one seen.</summary>
    private const int Attempts = 32;

    /// <summary>Clean windows that must measure the smallest count before it is the answer.</summary>
    private const int Confirmations = 3;

    /// <summary>
    /// The bytes the current thread allocates running <paramref name="work"/>: the smallest
    /// count measured, once <see cref="Confirmations"/> windows no garbage collection paused
    /// agree on it. <paramref name="work"/> runs several times, so it must allocate the same on
    /// every run (warm it up first).
    /// </summary>
    public static long BytesAllocatedBy(Action work)
    {
        long fewest = long.MaxValue;
        int confirmed = 0;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var paused = GC.GetTotalPauseDuration();
            long before = GC.GetAllocatedBytesForCurrentThread();
            work();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            bool clean = GC.GetTotalPauseDuration() == paused;

            // Noise inflates a window, never shrinks it: a smaller one replaces all before it.
            if (allocated > fewest)
                continue;
            if (allocated < fewest)
            {
                fewest = allocated;
                confirmed = 0;
            }

            // A window a collection paused bounds the answer from above but does not confirm it.
            if (clean && ++confirmed == Confirmations)
                break;
        }

        return fewest;
    }
}
