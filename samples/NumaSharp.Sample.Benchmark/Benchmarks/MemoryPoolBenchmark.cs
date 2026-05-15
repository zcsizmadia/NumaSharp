using System.Buffers;
using System.Diagnostics;
using NumaSharp.Core;
using NumaSharp.Scheduling;

namespace NumaSharp.Sample.Benchmark.Benchmarks;

/// <summary>
/// Benchmarks NUMA-local memory pool allocation, recycling, and memory binding performance.
/// </summary>
internal static class MemoryPoolBenchmark
{
    public static async Task RunAsync(TimeSpan duration)
    {
        NumaTopology topology = NumaTopology.Instance;
        NumaNode node0 = topology.Nodes[0];

        Console.WriteLine($"Memory pool benchmark — node 0: {node0.ProcessorCount} CPUs");

        await RunPoolAllocFree(node0, duration);
        await RunPoolRecycle(node0, duration);
        await RunPoolConcurrent(topology, duration);
    }

    private static async Task RunPoolAllocFree(NumaNode node, TimeSpan duration)
    {
        using NumaMemoryPool pool = new(node.NodeId, blockSize: 4096);
        using NumaNodeScheduler scheduler = new(node);

        var sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            await Task.Factory.StartNew(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    using IMemoryOwner<byte> owner = pool.Rent();
                    _ = owner.Memory.Span[0];
                    Interlocked.Increment(ref count);
                }
            }, CancellationToken.None, TaskCreationOptions.None, scheduler);
        }

        long opsPerSec = (long)(count / sw.Elapsed.TotalSeconds);
        Console.WriteLine($"  Alloc+free (4 KiB):    {opsPerSec:N0} ops/s");
    }

    private static async Task RunPoolRecycle(NumaNode node, TimeSpan duration)
    {
        using NumaMemoryPool pool = new(node.NodeId, blockSize: 4096);
        using NumaNodeScheduler scheduler = new(node);

        var sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            await Task.Factory.StartNew(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    IMemoryOwner<byte> owner = pool.Rent();
                    owner.Dispose(); // returns to pool
                    owner = pool.Rent(); // recycled
                    owner.Dispose();
                    Interlocked.Increment(ref count);
                }
            }, CancellationToken.None, TaskCreationOptions.None, scheduler);
        }

        long opsPerSec = (long)(count / sw.Elapsed.TotalSeconds);
        Console.WriteLine($"  Recycle (4 KiB):       {opsPerSec:N0} ops/s");
    }

    private static async Task RunPoolConcurrent(NumaTopology topology, TimeSpan duration)
    {
        using NumaTaskScheduler scheduler = new();
        long totalCount = 0;

        Task[] tasks = topology.Nodes.Select(node => Task.Run(async () =>
        {
            using NumaMemoryPool pool = new(node.NodeId, blockSize: 4096);
            var sw = Stopwatch.StartNew();
            long count = 0;

            while (sw.Elapsed < duration)
            {
                await Task.Factory.StartNew(() =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        using IMemoryOwner<byte> owner = pool.Rent();
                        Interlocked.Increment(ref count);
                    }
                }, CancellationToken.None, TaskCreationOptions.None, scheduler);
            }

            Interlocked.Add(ref totalCount, count);
        })).ToArray();

        await Task.WhenAll(tasks);

        long total = Interlocked.Read(ref totalCount);
        Console.WriteLine($"  Multi-node concurrent: {total:N0} ops total across {topology.NodeCount} node(s)");
    }
}
