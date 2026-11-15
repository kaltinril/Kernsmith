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
        var trimmed = new List<Array>();
        Drain<byte>(Size, trimmed);

        long allocated = AllocationMeter.BytesAllocatedBy(Work);

        GC.KeepAlive(trimmed);
        allocated.ShouldBe(0, "the refill after a pool trim was charged to the work");
    }

    [Fact]
    public void PooledScratch_IsNotCharged_WhenTwoPoolsAreTrimmedOneWindowApart()
    {
        // The colour-bitmap path rents from two pools (bytes for the PNG decoder, floats for the
        // resampler), and each pool trims from its own gen2 callback on the finalizer thread. The
        // measuring thread can run between the two: one window pays the byte refill, the very
        // next one the float refill, and only the windows after that are warm.
        const int Size = 8192;
        var trimmed = new List<Array>();
        int runs = 0;
        void Work()
        {
            if (++runs == 3) // the warm-up and the first window are behind us
                Drain<float>(Size, trimmed);

            byte[] bytes = ArrayPool<byte>.Shared.Rent(Size);
            float[] floats = ArrayPool<float>.Shared.Rent(Size);
            bytes[0] = 1;
            floats[0] = 1;
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<float>.Shared.Return(floats);
        }

        Work();
        Drain<byte>(Size, trimmed);

        long allocated = AllocationMeter.BytesAllocatedBy(Work);

        GC.KeepAlive(trimmed);
        runs.ShouldBeGreaterThanOrEqualTo(3, "the second trim never happened");
        allocated.ShouldBe(0, "two refills, one window apart, were charged to the work");
    }

    [Fact]
    public void RealAllocations_AreStillMeasured_WhenAPoolRefillInflatesSomeWindows()
    {
        // The same staggered refills around work that does allocate: the answer is the work's
        // own 4024 B, neither the inflated windows nor nothing.
        const int Size = 8192;
        var trimmed = new List<Array>();
        object? kept = null;
        int runs = 0;
        void Work()
        {
            if (++runs == 3)
                Drain<float>(Size, trimmed);

            byte[] bytes = ArrayPool<byte>.Shared.Rent(Size);
            float[] floats = ArrayPool<float>.Shared.Rent(Size);
            kept = new byte[4000];
            ArrayPool<byte>.Shared.Return(bytes);
            ArrayPool<float>.Shared.Return(floats);
        }

        Work();
        Drain<byte>(Size, trimmed);

        long allocated = AllocationMeter.BytesAllocatedBy(Work);

        GC.KeepAlive(trimmed);
        GC.KeepAlive(kept);
        allocated.ShouldBe(4024);
    }

    /// <summary>Empties the shared pool's bucket for <paramref name="size"/> on this thread, as
    /// its trim does: rents until a rent has to allocate, and keeps every array.</summary>
    private static void Drain<T>(int size, List<Array> taken)
    {
        while (true)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            taken.Add(ArrayPool<T>.Shared.Rent(size));
            if (GC.GetAllocatedBytesForCurrentThread() - before >= size)
                break;
        }
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
