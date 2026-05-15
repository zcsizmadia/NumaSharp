using NumaSharp.Core;
using NumaSharp.Sample.Benchmark;
using NumaSharp.Sample.Benchmark.Benchmarks;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("NumaSharp Performance Benchmark");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project samples/NumaSharp.Sample.Benchmark [options]");
    Console.WriteLine("  dotnet run -c Release --project samples/NumaSharp.Sample.Benchmark");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --quick        2s warmup / 5s measure per scenario");
    Console.WriteLine("  --scheduler    Benchmark 1: task scheduler throughput");
    Console.WriteLine("  --memory       Benchmark 2: NUMA-local memory pool");
    Console.WriteLine("  --cross        Benchmark 3: cross-node task dispatch");
    Console.WriteLine("  --scaleout     Benchmark 4: linear scale-out across NUMA nodes");
    Console.WriteLine("  --local        Benchmark 5: NumaLocal<T> read throughput");
    Console.WriteLine("  --help         Show this help");
    Console.WriteLine();
    Console.WriteLine("Default: runs all four benchmarks.");
    return;
}

NumaTopology topology = NumaTopology.Instance;
Display.PrintSystemInfo(topology);

bool quick = args.Contains("--quick");
TimeSpan measure = quick ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(10);

Console.WriteLine($"  Mode    : {(quick ? "quick" : "full")}");
Console.WriteLine($"  Measure : {measure.TotalSeconds:0}s per scenario");
Console.WriteLine();

bool runAll = !args.Any(static a => a is "--scheduler" or "--memory" or "--cross" or "--scaleout" or "--local");

if (runAll || args.Contains("--scheduler"))
{
    await SchedulerThroughputBenchmark.RunAsync(measure);
}

if (runAll || args.Contains("--memory"))
{
    await MemoryPoolBenchmark.RunAsync(measure);
}

if (runAll || args.Contains("--cross"))
{
    await CrossNodeBenchmark.RunAsync(measure);
}

if (runAll || args.Contains("--scaleout"))
{
    await ScaleOutBenchmark.RunAsync(measure);
}

if (runAll || args.Contains("--local"))
{
    await NumaLocalBenchmark.RunAsync(measure);
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  All benchmarks complete.");
Console.ResetColor();
