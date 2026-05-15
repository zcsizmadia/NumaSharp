using System.Diagnostics;
using NumaSharp.Core;
using NumaSharp.Scheduling;

namespace NumaSharp.Sample.Benchmark.Benchmarks;

/// <summary>
/// Benchmarks <see cref="NumaTaskScheduler"/> task throughput across different
/// threading configurations.
/// </summary>
internal static class SchedulerThroughputBenchmark
{
    public static async Task RunAsync(TimeSpan duration)
    {
        NumaTopology topology = NumaTopology.Instance;
        Console.WriteLine($"Scheduler throughput benchmark — {topology.NodeCount} NUMA node(s)");

        await RunUnpinned(duration);
        await RunPinned(topology, duration);
        await RunAllNodes(topology, duration);
    }

    private static async Task RunUnpinned(TimeSpan duration)
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.RoundRobin);
        long opsPerSec = await MeasureRoundRobin(scheduler, duration);
        Console.WriteLine($"  Round-robin (unpinned): {opsPerSec:N0} tasks/s");
    }

    private static async Task RunPinned(NumaTopology topology, TimeSpan duration)
    {
        NumaNode node = topology.Nodes[0];
        using NumaNodeScheduler nodeScheduler = new(node);

        var sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            await Task.Factory.StartNew(
                () => Interlocked.Increment(ref count),
                CancellationToken.None,
                TaskCreationOptions.None,
                nodeScheduler);
        }

        long opsPerSec = (long)(count / sw.Elapsed.TotalSeconds);
        Console.WriteLine($"  Per-node (node 0):      {opsPerSec:N0} tasks/s");
    }

    private static async Task RunAllNodes(NumaTopology topology, TimeSpan duration)
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LeastLoaded);
        long opsPerSec = await MeasureRoundRobin(scheduler, duration);
        Console.WriteLine($"  LeastLoaded policy:     {opsPerSec:N0} tasks/s");
    }

    private static async Task<long> MeasureRoundRobin(NumaTaskScheduler scheduler, TimeSpan duration)
    {
        var sw = Stopwatch.StartNew();
        long count = 0;
        int nodeCount = scheduler.NodeCount;

        while (sw.Elapsed < duration)
        {
            int node = (int)(count % nodeCount);
            await scheduler.RunOnNode(node, () => Interlocked.Increment(ref count));
        }

        return (long)(count / sw.Elapsed.TotalSeconds);
    }
}
