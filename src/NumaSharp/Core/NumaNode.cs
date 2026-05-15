using NumaSharp.Core.Platform;

namespace NumaSharp.Core;

/// <summary>
/// Represents a single NUMA node and its associated CPUs and memory.
/// </summary>
public sealed class NumaNode
{
    /// <summary>Gets the NUMA node index (0-based).</summary>
    public int NodeId { get; }

    /// <summary>Gets the number of logical processors on this node.</summary>
    public int ProcessorCount { get; }

    /// <summary>Gets the logical CPU IDs that belong to this node.</summary>
    public IReadOnlyList<int> CpuIds { get; }

    /// <summary>Gets the total memory available on this node in bytes, or 0 if unavailable.</summary>
    public long MemoryBytes { get; }

    /// <summary>Platform-specific affinity data used for thread pinning.</summary>
    internal NumaAffinityInfo AffinityInfo { get; }

    internal NumaNode(NumaAffinityInfo info, long memoryBytes = 0)
    {
        AffinityInfo   = info;
        NodeId         = info.NodeId;
        ProcessorCount = info.ProcessorCount;
        CpuIds         = info.CpuIds;
        MemoryBytes    = memoryBytes;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"NUMA Node {NodeId} | {ProcessorCount} logical CPU(s){AffinityInfo.GetDescription()}" +
        (MemoryBytes > 0 ? $" | {MemoryBytes / (1024 * 1024)} MB" : string.Empty);
}
