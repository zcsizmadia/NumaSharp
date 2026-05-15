namespace NumaSharp.Sample.KestrelBenchmark;

/// <summary>Command-line options parsed at startup.</summary>
internal sealed class BenchmarkConfig
{
    public int WarmupSeconds { get; init; } = 5;
    public int MeasureSeconds { get; init; } = 15;
    public int Concurrency { get; init; } = DefaultConcurrency();
    public bool RunBaseline { get; init; } = true;
    public bool RunEpoll { get; init; } = true;

    private static int DefaultConcurrency() =>
        Math.Min(Environment.ProcessorCount * 2, 512);

    public static BenchmarkConfig Parse(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            Environment.Exit(0);
        }

        bool quick = args.Contains("--quick");
        bool hasFilter = args.Contains("--baseline") || args.Contains("--epoll");

        return new BenchmarkConfig
        {
            WarmupSeconds = quick ? 2 : 5,
            MeasureSeconds = quick ? 5 : 15,
            Concurrency = ParseConnections(args),
            RunBaseline = !hasFilter || args.Contains("--baseline"),
            RunEpoll = !hasFilter || args.Contains("--epoll"),
        };
    }

    private static int ParseConnections(string[] args)
    {
        int idx = Array.IndexOf(args, "--connections");
        if (idx >= 0 && idx + 1 < args.Length
            && int.TryParse(args[idx + 1], out int n) && n > 0)
        {
            return n;
        }

        return DefaultConcurrency();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""

  Usage: dotnet run -c Release --project samples/NumaSharp.Sample.Kestrel.Benchmark [options]

  Options:
    --quick               Short run (2 s warmup / 5 s measure per endpoint)
    --baseline            Run Kestrel default transport only
    --epoll               Run NumaSharp Epoll transport only
    --connections <N>     Number of keep-alive connections (default: min(cpus*2, 512))
    --help, -h            Print this message

  Filters may be combined: --quick --baseline --epoll

""");
    }
}
