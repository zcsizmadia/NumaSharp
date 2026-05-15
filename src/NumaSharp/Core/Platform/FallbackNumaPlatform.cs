namespace NumaSharp.Core.Platform;

/// <summary>
/// NUMA platform stub for macOS and any OS without dedicated NUMA APIs.
/// Reports a single virtual NUMA node containing all logical processors.
/// Thread pinning and node detection are no-ops.
/// </summary>
internal sealed class FallbackAffinityInfo : NumaAffinityInfo
{
    internal FallbackAffinityInfo(int nodeId, int processorCount, int[] cpuIds)
        : base(nodeId, processorCount, cpuIds) { }
}

/// <summary>
/// NUMA platform implementation for macOS and other platforms without NUMA APIs.
/// </summary>
internal sealed class FallbackNumaPlatform : INumaPlatform
{
    public IReadOnlyList<NumaAffinityInfo> DiscoverNodes()
    {
        int n = Environment.ProcessorCount;
        int[] cpuIds = new int[n];
        for (int i = 0; i < n; i++)
        {
            cpuIds[i] = i;
        }

        return [new FallbackAffinityInfo(0, n, cpuIds)];
    }

    public void PinCurrentThreadToNode(NumaAffinityInfo info)
    {
        // No user-space thread-pinning API available on this platform.
    }

    public int GetCurrentNodeId() => 0;
}
