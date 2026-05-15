namespace NumaSharp.Sample.KestrelBenchmark;

/// <summary>Renders benchmark results as a formatted console table.</summary>
internal static class ResultsDisplay
{
    private const int CScenario = 33;
    private const int CEndpoint = 22;
    private const int CRps = 13;
    private const int CMbps = 8;
    private const int CLatency = 9;
    private const int CVs = 13;

    public static void PrintSummary(List<BenchmarkResult> results)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(
            "  ═══ Results ══════════════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.ResetColor();
        Console.WriteLine();

        string header =
            $"  {"Scenario",-CScenario}  {"Endpoint",-CEndpoint}  {"RPS",CRps}  {"MB/s",CMbps}" +
            $"  {"P50",CLatency}  {"P95",CLatency}  {"P99",CLatency}  {"vs Baseline",CVs}";

        string separator =
            $"  {new string('─', CScenario)}  {new string('─', CEndpoint)}  {new string('─', CRps)}" +
            $"  {new string('─', CMbps)}  {new string('─', CLatency)}  {new string('─', CLatency)}" +
            $"  {new string('─', CLatency)}  {new string('─', CVs)}";

        Console.WriteLine(header);
        Console.WriteLine(separator);

        IEnumerable<IGrouping<string, BenchmarkResult>> groups =
            results.GroupBy(static r => r.EndpointLabel);

        bool firstGroup = true;
        foreach (IGrouping<string, BenchmarkResult> group in groups)
        {
            if (!firstGroup)
            {
                Console.WriteLine(separator);
            }

            firstGroup = false;
            double baselineRps = 0;
            bool baselineSet = false;

            foreach (BenchmarkResult r in group)
            {
                if (r.Skipped)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(
                        $"  {r.ScenarioLabel,-CScenario}  {r.EndpointLabel,-CEndpoint}  " +
                        $"⏭  Skipped: {r.SkipReason}");
                    Console.ResetColor();
                    continue;
                }

                string vsLabel;
                bool faster = false;

                if (!baselineSet)
                {
                    baselineRps = r.RequestsPerSecond;
                    baselineSet = true;
                    vsLabel = "baseline";
                }
                else
                {
                    double multiplier = baselineRps > 0
                        ? r.RequestsPerSecond / baselineRps
                        : 1.0;
                    vsLabel = $"{multiplier:F2}×";
                    faster = multiplier > 1.05;
                }

                if (faster)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                }

                Console.WriteLine(
                    $"  {r.ScenarioLabel,-CScenario}  {r.EndpointLabel,-CEndpoint}" +
                    $"  {r.RequestsPerSecond,CRps:N0}" +
                    $"  {r.MegabytesPerSecond,CMbps:F1}" +
                    $"  {FormatLatency(r.P50Ns),CLatency}" +
                    $"  {FormatLatency(r.P95Ns),CLatency}" +
                    $"  {FormatLatency(r.P99Ns),CLatency}" +
                    $"  {vsLabel,CVs}{(faster ? "  ▲" : string.Empty)}");

                if (faster)
                {
                    Console.ResetColor();
                }
            }
        }

        Console.WriteLine(separator);
        Console.WriteLine();
    }

    private static string FormatLatency(long ns) =>
        ns switch
        {
            < 1_000 => $"{ns} ns",
            < 1_000_000 => $"{ns / 1_000.0:F0} µs",
            _ => $"{ns / 1_000_000.0:F1} ms",
        };
}
