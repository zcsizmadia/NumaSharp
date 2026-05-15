using System.Runtime.InteropServices;
using NumaSharp.Core;

namespace NumaSharp.Sample.Benchmark;

internal static class Display
{
    private const int ScenarioWidth = 50;
    private const int ThroughputWidth = 18;
    private const int LatencyWidth = 12;
    private const int RatioWidth = 12;

    public static void PrintSystemInfo(NumaTopology topology)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  ╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("  ║         NumaSharp — Performance Benchmark                    ║");
        Console.WriteLine("  ╚══════════════════════════════════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"  OS         : {RuntimeInformation.OSDescription}");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // RuntimeInformation.OSDescription on .NET 10+ returns the distro pretty
            // name (e.g. "Ubuntu 22.04.5 LTS"), not the uname kernel string.
            // Read the kernel release directly from procfs instead.
            const string osReleasePath = "/proc/sys/kernel/osrelease";
            string kernelVer = File.Exists(osReleasePath)
                ? File.ReadAllText(osReleasePath).Trim()
                : Environment.OSVersion.Version.ToString(3);
            Console.WriteLine($"  Kernel     : {kernelVer}");
        }

        Console.WriteLine($"  .NET       : {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"  CPU Cores  : {Environment.ProcessorCount}");
        Console.WriteLine($"  NUMA Nodes : {topology.NodeCount}");

        foreach (NumaNode node in topology.Nodes)
        {
            string cpuRange = node.CpuIds.Count == 0
                ? "(no CPUs)"
                : FormatCpuRange(node.CpuIds);
            string mem = node.MemoryBytes > 0
                ? $"{node.MemoryBytes / (1024L * 1024 * 1024)} GB"
                : "N/A";
            Console.WriteLine($"    Node {node.NodeId} : {cpuRange}   Mem: {mem}");
        }

        Console.WriteLine();
    }

    private static string FormatCpuRange(IReadOnlyList<int> cpuIds)
    {
        if (cpuIds.Count == 1)
        {
            return $"1 CPU ({cpuIds[0]})";
        }

        // Detect whether the list is contiguous (stride 1) or has a uniform stride.
        int stride = cpuIds[1] - cpuIds[0];
        bool uniformStride = stride > 0;
        for (int i = 2; i < cpuIds.Count && uniformStride; i++)
        {
            if (cpuIds[i] - cpuIds[i - 1] != stride)
            {
                uniformStride = false;
            }
        }

        if (uniformStride && stride == 1)
        {
            return $"CPUs {cpuIds[0]}\u2013{cpuIds[^1]} ({cpuIds.Count} cores)";
        }

        if (uniformStride)
        {
            // e.g. "48 CPUs (stride 2: 0, 2, … 94)"
            return $"{cpuIds.Count} CPUs (stride {stride}: {cpuIds[0]}, {cpuIds[1]}, \u2026 {cpuIds[^1]})";
        }

        // Irregular layout — just show count and range.
        return $"{cpuIds.Count} CPUs (non-contiguous, {cpuIds[0]}\u2013{cpuIds[^1]})";
    }

    public static void PrintBenchmarkHeader(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        Console.WriteLine($"  {title}");
        Console.WriteLine($"  \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine(
            $"  {"Scenario",-ScenarioWidth}  {"Throughput",ThroughputWidth}  {"Latency",LatencyWidth}  {"vs Baseline",RatioWidth}");
        Console.WriteLine($"  {new string('\u2500', ScenarioWidth + ThroughputWidth + LatencyWidth + RatioWidth + 8)}");
    }

    public static void PrintResult(MeasureResult result, long baselineOpsPerSec = 0)
    {
        string throughput = $"{result.OpsPerSecond:#,##0} ops/s";
        string latency = $"{result.LatencyNs:F1} ns";
        string ratio = baselineOpsPerSec > 0
            ? $"{result.OpsPerSecond / (double)baselineOpsPerSec:F2}\u00d7"
            : "baseline";

        bool faster = baselineOpsPerSec > 0 && result.OpsPerSecond > baselineOpsPerSec;
        if (faster)
        {
            Console.ForegroundColor = ConsoleColor.Green;
        }

        Console.WriteLine(
            $"  {result.Scenario,-ScenarioWidth}  {throughput,ThroughputWidth}  {latency,LatencyWidth}  {ratio,RatioWidth}");
        Console.ResetColor();
    }

    public static void PrintNote(string note)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  \u2139  {note}");
        Console.ResetColor();
    }

    public static void PrintSkipped(string reason)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  \u23ed  Skipped: {reason}");
        Console.ResetColor();
    }
}
