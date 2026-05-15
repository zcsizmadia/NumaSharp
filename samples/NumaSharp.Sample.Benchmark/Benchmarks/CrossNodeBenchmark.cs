using System.Diagnostics;
using NumaSharp.Core;
using NumaSharp.Scheduling;

namespace NumaSharp.Sample.Benchmark.Benchmarks;

/// <summary>
/// Benchmarks cross-NUMA-node task dispatch latency using <see cref="NumaTaskScheduler"/>.
/// </summary>
internal static class CrossNodeBenchmark
{
    public static async Task RunAsync(TimeSpan duration)
    {
        NumaTopology topology = NumaTopology.Instance;
        Console.WriteLine($"Cross-node benchmark — {topology.NodeCount} NUMA node(s)");

        await RunSingleNodeDispatch(topology, duration);

        if (topology.NodeCount >= 2)
        {
            await RunCrossNodeDispatch(topology, duration);
        }
        else
        {
            Console.WriteLine("  Skipped cross-node (only 1 NUMA node).");
        }
    }

    private static async Task RunSingleNodeDispatch(NumaTopology topology, TimeSpan duration)
    {
        using NumaTaskScheduler scheduler = new();
        long ops = await MeasureOpsPerSecond("Single-node dispatch", scheduler, nodeIndex: 0, duration);
        Console.WriteLine($"  Single-node dispatch: {ops:N0} tasks/s");
    }

    private static async Task RunCrossNodeDispatch(NumaTopology topology, TimeSpan duration)
    {
        using NumaTaskScheduler scheduler = new();

        var sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            // Alternate between node 0 and node 1.
            int node = (int)(count % 2);
            await scheduler.RunOnNode(node, () => Interlocked.Increment(ref count));
        }

        double secs = sw.Elapsed.TotalSeconds;
        long opsPerSec = (long)(count / secs);
        Console.WriteLine($"  Cross-node dispatch:  {opsPerSec:N0} tasks/s");
    }

    private static async Task<long> MeasureOpsPerSecond(string name, NumaTaskScheduler scheduler, int nodeIndex, TimeSpan duration)
    {
        var sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            await scheduler.RunOnNode(nodeIndex, () => Interlocked.Increment(ref count));
        }

        double secs = sw.Elapsed.TotalSeconds;
        return (long)(count / secs);
    }
}
