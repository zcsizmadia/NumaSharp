// ── NumaSharp — Kestrel Transport Benchmark ──────────────────────────────────
//
// Compares Kestrel's built-in SocketTransport against NumaSharp's Epoll transport
// using an in-process HTTP/1.1 load generator.
//
// Three endpoint shapes per scenario:
//   GET /ping          — tiny 4-byte response, measures pure RPS
//   GET /data (64 KB)  — fixed-size response, measures download bandwidth
//   POST /echo (*)     — request body echoed back, measures pipeline throughput
//
// Usage:
//   dotnet run -c Release --project samples/NumaSharp.Sample.Kestrel.Benchmark
//   dotnet run -c Release --project samples/NumaSharp.Sample.Kestrel.Benchmark -- --quick
//   dotnet run -c Release --project samples/NumaSharp.Sample.Kestrel.Benchmark -- --help

using System.Runtime.InteropServices;
using NumaSharp.Core;
using NumaSharp.Sample.KestrelBenchmark;

BenchmarkConfig config = BenchmarkConfig.Parse(args);

// ── Header ────────────────────────────────────────────────────────────────────

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine();
Console.WriteLine("  ╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("  ║       NumaSharp — Kestrel Transport Benchmark             ║");
Console.WriteLine("  ╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.ResetColor();

// ── System info ───────────────────────────────────────────────────────────────

NumaTopology topology = NumaTopology.Instance;

Console.WriteLine($"  .NET:         {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"  OS:           {RuntimeInformation.OSDescription}");
if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    const string osReleasePath = "/proc/sys/kernel/osrelease";
    string kernelVer = File.Exists(osReleasePath)
        ? File.ReadAllText(osReleasePath).Trim()
        : Environment.OSVersion.Version.ToString(3);
    Console.WriteLine($"  Kernel:       {kernelVer}");
}
Console.WriteLine($"  NUMA nodes:   {topology.NodeCount}");
Console.WriteLine($"  Total CPUs:   {topology.Nodes.Sum(n => n.CpuIds.Count)}");
Console.WriteLine($"  Concurrency:  {config.Concurrency} keep-alive connections");
Console.WriteLine($"  Warmup:       {config.WarmupSeconds} s  |  Measure: {config.MeasureSeconds} s per endpoint");
Console.WriteLine();

// ── Scenarios ─────────────────────────────────────────────────────────────────

int port = 15880;
List<BenchmarkResult> allResults = [];

using CancellationTokenSource cts = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

foreach (KestrelScenario scenario in KestrelScenario.CreateAll(config))
{
    if (cts.Token.IsCancellationRequested)
    {
        break;
    }

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  ── {scenario.Label}");
    Console.ResetColor();

    List<BenchmarkResult> results = await scenario.RunAsync(config, port++, cts.Token);
    allResults.AddRange(results);
    Console.WriteLine();
}

// ── Summary table ─────────────────────────────────────────────────────────────

ResultsDisplay.PrintSummary(allResults);
