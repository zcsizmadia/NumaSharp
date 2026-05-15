namespace NumaSharp.Sample.Benchmark;

/// <summary>Result of one timed benchmark scenario.</summary>
internal sealed class MeasureResult(string scenario, long totalOps, double elapsedSeconds)
{
    public string Scenario { get; } = scenario;
    public long TotalOps { get; } = totalOps;

    /// <summary>Derived: operations per second.</summary>
    public long OpsPerSecond { get; } =
        elapsedSeconds > 0 ? (long)(totalOps / elapsedSeconds) : 0;

    /// <summary>Derived: average nanoseconds per operation.</summary>
    public double LatencyNs { get; } =
        totalOps > 0 ? elapsedSeconds * 1_000_000_000.0 / totalOps : 0;
}
