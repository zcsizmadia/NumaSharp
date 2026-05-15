namespace NumaSharp.Sample.KestrelBenchmark;

/// <summary>Immutable result from one scenario + endpoint combination.</summary>
internal sealed class BenchmarkResult
{
    public string ScenarioLabel { get; init; } = string.Empty;
    public string EndpointLabel { get; init; } = string.Empty;
    public double RequestsPerSecond { get; init; }
    public double MegabytesPerSecond { get; init; }
    public long P50Ns { get; init; }
    public long P95Ns { get; init; }
    public long P99Ns { get; init; }

    public bool Skipped { get; init; }
    public string SkipReason { get; init; } = string.Empty;

    public static BenchmarkResult CreateSkipped(
        string scenario,
        string endpoint,
        string reason) =>
        new()
        {
            ScenarioLabel = scenario,
            EndpointLabel = endpoint,
            Skipped = true,
            SkipReason = reason,
        };
}
