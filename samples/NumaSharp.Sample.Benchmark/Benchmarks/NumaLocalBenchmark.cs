using System.Diagnostics;
using System.Runtime.CompilerServices;
using NumaSharp.Core;

namespace NumaSharp.Sample.Benchmark.Benchmarks;

/// <summary>
/// Benchmarks <see cref="NumaLocal{T}"/> read throughput against comparable
/// baseline patterns: a plain field read, a <c>ThreadLocal&lt;T&gt;</c> read, and a
/// locked shared-dictionary lookup.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this measures</b><br/>
/// The hot-path cost of <c>NumaLocal&lt;T&gt;.Value</c> is one array read indexed by
/// <see cref="NumaTopology.GetCurrentNodeIndex()"/>. These scenarios put that in
/// context by showing how it compares to the alternatives that callers would otherwise
/// use for per-node state.
/// </para>
/// </remarks>
internal static class NumaLocalBenchmark
{
    public static async Task RunAsync(TimeSpan duration)
    {
        NumaTopology topology = NumaTopology.Instance;
        Console.WriteLine($"NumaLocal benchmark — {topology.NodeCount} NUMA node(s), {Environment.ProcessorCount} CPUs");
        Console.WriteLine();

        await RunLocalRead(duration);
        await RunLocalReadPinned(duration);
        await RunThreadLocalRead(duration);
        await RunLockedDictionaryRead(duration);
        await RunLocalReadMultiThread(duration);
        await RunAggregation(topology, duration);

        Console.WriteLine();
    }

    // ── 1. NumaLocal.Value — single thread ────────────────────────────────────

    private static Task RunLocalRead(TimeSpan duration)
    {
        using NumaLocal<long[]> local = new((NumaNode _) => new long[1]);

        Stopwatch sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            for (int i = 0; i < 10_000; i++)
            {
                local.Value[0]++;
                count++;
            }
        }

        PrintRow("NumaLocal.Value  (single-thread)", count, sw.Elapsed);
        return Task.CompletedTask;
    }

    // ── 1b. NumaLocal.Value — single thread, pinned-hint (no sched_getcpu) ───────

    private static Task RunLocalReadPinned(TimeSpan duration)
    {
        using NumaLocal<long[]> local = new((NumaNode _) => new long[1]);

        // Simulate a pinned reactor thread: set the hint once so Value skips the
        // sched_getcpu() syscall on every access.
        int currentIndex = NumaTopology.Instance.GetCurrentNodeIndex();
        NumaTopology.SetCurrentNodeIndexHint(currentIndex);
        try
        {
            Stopwatch sw = Stopwatch.StartNew();
            long count = 0;

            while (sw.Elapsed < duration)
            {
                for (int i = 0; i < 10_000; i++)
                {
                    local.Value[0]++;
                    count++;
                }
            }

            PrintRow("NumaLocal.Value  (pinned, single-thread)", count, sw.Elapsed);
        }
        finally
        {
            NumaTopology.ClearNodeIndexHint();
        }

        return Task.CompletedTask;
    }

    // ── 2. ThreadLocal<T> — single thread (baseline comparison) ──────────────

    private static Task RunThreadLocalRead(TimeSpan duration)
    {
        using ThreadLocal<long[]> tl = new(() => new long[1]);

        Stopwatch sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            for (int i = 0; i < 10_000; i++)
            {
                tl.Value![0]++;
                count++;
            }
        }

        PrintRow("ThreadLocal.Value (single-thread)", count, sw.Elapsed);
        return Task.CompletedTask;
    }

    // ── 3. lock + Dictionary — single thread (naive shared-state baseline) ────

    private static Task RunLockedDictionaryRead(TimeSpan duration)
    {
        Dictionary<int, long[]> dict = new()
        {
            [0] = new long[1],
        };
        object lockObj = new();

        Stopwatch sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            for (int i = 0; i < 10_000; i++)
            {
                lock (lockObj)
                {
                    dict[0][0]++;
                }

                count++;
            }
        }

        PrintRow("lock+Dictionary  (single-thread)", count, sw.Elapsed);
        return Task.CompletedTask;
    }

    // ── 4. NumaLocal.Value — all logical CPUs, parallel ──────────────────────

    private static async Task RunLocalReadMultiThread(TimeSpan duration)
    {
        using NumaLocal<long[]> local = new((NumaNode _) => new long[1]);
        int threads = Environment.ProcessorCount;
        long totalCount = 0;

        Task[] tasks = new Task[threads];
        for (int t = 0; t < threads; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                long count = 0;
                Stopwatch sw = Stopwatch.StartNew();

                while (sw.Elapsed < duration)
                {
                    for (int i = 0; i < 10_000; i++)
                    {
                        local.Value[0]++;
                        count++;
                    }
                }

                Interlocked.Add(ref totalCount, count);
            });
        }

        await Task.WhenAll(tasks);

        PrintRow($"NumaLocal.Value  ({threads} threads, all CPUs)", totalCount, duration);
    }

    // ── 5. Aggregation cost ───────────────────────────────────────────────────

    private static Task RunAggregation(NumaTopology topology, TimeSpan duration)
    {
        using NumaLocal<long[]> local = new((NumaNode _) =>
        {
            long[] arr = new long[1];
            arr[0] = 42L;
            return arr;
        });

        Stopwatch sw = Stopwatch.StartNew();
        long count = 0;

        while (sw.Elapsed < duration)
        {
            for (int i = 0; i < 1_000; i++)
            {
                long sum = local.Aggregate(static arr => arr[0], static (a, b) => a + b, 0L);
                // Prevent optimisation of the aggregate result.
                [MethodImpl(MethodImplOptions.NoInlining)]
                static void Sink(long _) { }
                Sink(sum);
                count++;
            }
        }

        PrintRow($"NumaLocal.Aggregate ({topology.NodeCount} node(s))", count, sw.Elapsed);
        return Task.CompletedTask;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static void PrintRow(string label, long ops, TimeSpan elapsed)
    {
        long opsPerSec = elapsed.TotalSeconds > 0
            ? (long)(ops / elapsed.TotalSeconds)
            : 0;
        Console.WriteLine($"  {label,-45}  {opsPerSec,16:N0} ops/s");
    }
}
