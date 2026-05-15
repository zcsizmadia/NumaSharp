using System.Diagnostics;
using NumaSharp.Core;
using NumaSharp.Scheduling;

namespace NumaSharp.Sample.Benchmark.Benchmarks;

/// <summary>
/// Benchmarks how <see cref="NumaTaskScheduler"/> scales across NUMA nodes
/// by dispatching concurrent workloads.
/// </summary>
internal static class ScaleOutBenchmark
{
    public static async Task RunAsync(TimeSpan duration)
    {
        NumaTopology topology = NumaTopology.Instance;
        int nodeCount = topology.NodeCount;
        Console.WriteLine($"Scale-out benchmark — {nodeCount} NUMA node(s)");

        await RunSingleNode(topology, duration);

        if (nodeCount >= 2)
        {
            await RunTwoNodes(topology, duration);
        }
        else
        {
            Console.WriteLine("  Skipped dual-node (only 1 NUMA node).");
        }

        await RunAllNodes(topology, duration);
    }

    private static async Task RunSingleNode(NumaTopology topology, TimeSpan duration)
    {
        using NumaNodeScheduler scheduler = new(topology.Nodes[0], threadCount: 4);
        long opsPerSec = await MeasureScheduler(scheduler, duration);
        Console.WriteLine($"  1 node, 4 threads:  {opsPerSec:N0} tasks/s");
    }

    private static async Task RunTwoNodes(NumaTopology topology, TimeSpan duration)
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.RoundRobin, threadsPerNode: 4);
        long opsPerSec = await MeasureMultiNode(scheduler, 2, duration);
        Console.WriteLine($"  2 nodes, 4t each:   {opsPerSec:N0} tasks/s");
    }

    private static async Task RunAllNodes(NumaTopology topology, TimeSpan duration)
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LeastLoaded);
        long opsPerSec = await MeasureMultiNode(scheduler, scheduler.NodeCount, duration);
        Console.WriteLine($"  All nodes, default: {opsPerSec:N0} tasks/s");
    }

    private static async Task<long> MeasureScheduler(NumaNodeScheduler scheduler, TimeSpan duration)
    {
        var sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            await Task.Factory.StartNew(
                () => Interlocked.Increment(ref count),
                CancellationToken.None,
                TaskCreationOptions.None,
                scheduler);
        }

        return (long)(count / sw.Elapsed.TotalSeconds);
    }

    private static async Task<long> MeasureMultiNode(NumaTaskScheduler scheduler, int nodeCount, TimeSpan duration)
    {
        // Spin up one producer Task per node so each node gets dedicated dispatch
        // throughput — mirroring the single-node measurement which also has one producer.
        // The previous single-threaded round-robin loop measured cross-node dispatch
        // latency, not scale-out throughput.
        long totalCount = 0;
        Task[] producers = new Task[nodeCount];

        for (int n = 0; n < nodeCount; n++)
        {
            int nodeIndex = n % scheduler.NodeCount;
            producers[n] = Task.Run(async () =>
            {
                long count = 0;
                Stopwatch sw = Stopwatch.StartNew();

                while (sw.Elapsed < duration)
                {
                    await scheduler.RunOnNode(nodeIndex, () => Interlocked.Increment(ref count))
                        .ConfigureAwait(false);
                }

                Interlocked.Add(ref totalCount, count);
            });
        }

        await Task.WhenAll(producers).ConfigureAwait(false);
        return (long)(totalCount / duration.TotalSeconds);
    }
}
