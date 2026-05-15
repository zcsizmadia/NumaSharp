using System.Runtime.CompilerServices;

namespace NumaSharp.Sample.KestrelBenchmark;

/// <summary>
/// Thread-safe, lock-free latency histogram.
/// Accumulates up to <c>MaxSamples</c> nanosecond measurements, then saturates
/// (older samples are kept; new samples beyond the cap are silently dropped).
/// Call <see cref="Percentile"/> only after all workers have finished.
/// </summary>
internal sealed class LatencyHistogram
{
    private const int MaxSamples = 200_000;

    private readonly long[] _samples = new long[MaxSamples];
    private int _head;

    /// <summary>Records one latency sample in nanoseconds.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(long nanoseconds)
    {
        int idx = Interlocked.Increment(ref _head) - 1;
        if ((uint)idx < MaxSamples)
        {
            _samples[idx] = nanoseconds;
        }
    }

    /// <summary>
    /// Returns the <paramref name="p"/>-th percentile (e.g. 99.0 for P99) in nanoseconds.
    /// Sorts a copy of the collected samples; safe to call once from a single thread.
    /// </summary>
    public long Percentile(double p)
    {
        int count = Math.Min(Volatile.Read(ref _head), MaxSamples);
        if (count == 0)
        {
            return 0;
        }

        long[] copy = _samples[..count];
        Array.Sort(copy);
        int idx = Math.Clamp((int)(p / 100.0 * count), 0, count - 1);
        return copy[idx];
    }
}
