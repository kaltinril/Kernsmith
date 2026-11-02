using System.Buffers;
using Shouldly;

namespace KernSmith.Rasterizers.Native.Tests;

/// <summary>
/// The allocation ceilings (<see cref="NativeAllocationTests"/> and friends) must hold under a
/// full, parallel test run: garbage collections other tests trigger must not read as allocations,
/// nor must the shared array pool's refill after one trimmed it, and real allocations must never
/// be hidden.
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

    [Fact]
    public void PooledScratch_IsNotCharged_WhenTheSharedPoolWasTrimmedBeforeTheWindow()
    {
        // Work whose only buffer is pooled scratch: allocation-free once the pool holds the
        // array, as every pooled stage of the rasterizer is.
        const int Size = 8192;
        void Work()
        {
            byte[] scratch = ArrayPool<byte>.Shared.Rent(Size);
            scratch[0] = 1;
            ArrayPool<byte>.Shared.Return(scratch);
        }

        Work();
        AllocationMeter.BytesAllocatedBy(Work).ShouldBe(0, "the warm pool should serve the scratch");

        // A gen2 collection's trim callback empties the thread-local and per-core slots behind
        // the thread's back, with no pause inside the window that follows: take the bucket's
        // arrays the same way, and keep them.
        var trimmed = new List<byte[]>();
        while (true)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            trimmed.Add(ArrayPool<byte>.Shared.Rent(Size));
            if (GC.GetAllocatedBytesForCurrentThread() - before >= Size)
                break;
        }

        long allocated = AllocationMeter.BytesAllocatedBy(Work);

        GC.KeepAlive(trimmed);
        allocated.ShouldBe(0, "the refill after a pool trim was charged to the work");
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
