using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// The allocation ceilings (<see cref="NativeAllocationTests"/> and friends) must hold under a
/// full, parallel test run: garbage collections other tests trigger must not read as allocations,
/// and real allocations must never be hidden.
/// </summary>
public class AllocationMeterTests
{
    private const int Windows = 400;

    [Fact]
    public void AllocationFreeWork_MeasuresZero_WhileOtherThreadsCollect()
    {
        int[] data = Enumerable.Range(0, 20_000).ToArray();
        long sum = 0;
        void Work()
        {
            foreach (int value in data)
                sum += value;
        }

        var readings = UnderCollectionPressure(() => AllocationMeter.BytesAllocatedBy(Work));

        GC.KeepAlive(sum);
        readings.Where(r => r != 0).ShouldBeEmpty("an allocation-free window was charged for a garbage collection");
    }

    [Fact]
    public void RealAllocations_AreStillMeasured_WhileOtherThreadsCollect()
    {
        object? kept = null;
        void Work() => kept = new byte[4000];

        var readings = UnderCollectionPressure(() => AllocationMeter.BytesAllocatedBy(Work));

        GC.KeepAlive(kept);
        readings.ShouldAllBe(r => r >= 4024, "a 4000 byte array (24 bytes of header) went unmeasured");
    }

    /// <summary>Takes <see cref="Windows"/> readings while other threads fill the large object
    /// heap, which keeps background (concurrent) gen2 collections running.</summary>
    private static long[] UnderCollectionPressure(Func<long> measure)
    {
        measure(); // JIT and first-call costs out of the way
        using var stop = new CancellationTokenSource();
        var allocators = Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount / 2)).Select(_ => new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
                GC.KeepAlive(new byte[200_000]);
        })).ToArray();
        foreach (var allocator in allocators)
            allocator.Start();
        try
        {
            var readings = new long[Windows];
            for (int i = 0; i < readings.Length; i++)
            {
                GC.KeepAlive(new object()); // a fresh allocation context for a collection to retire
                readings[i] = measure();
            }

            return readings;
        }
        finally
        {
            stop.Cancel();
            foreach (var allocator in allocators)
                allocator.Join();
        }
    }
}
