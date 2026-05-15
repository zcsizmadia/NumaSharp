using System.Runtime.InteropServices;
using NumaSharp.Core.Platform;

namespace NumaSharp.Core;

/// <summary>
/// Discovers and caches the NUMA topology of the current system.
/// Access the singleton via <see cref="Instance"/>.
/// Supports Windows (processor groups), Linux (sysfs + sched_setaffinity),
/// and other platforms (single-node graceful fallback).
/// </summary>
public sealed class NumaTopology
{
    private static readonly Lazy<NumaTopology> s_lazy =
        new(() => new NumaTopology(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Process-wide singleton; lazily initialised on first access.</summary>
    public static NumaTopology Instance => s_lazy.Value;

    private readonly INumaPlatform _platform;
    private readonly NumaNode[] _nodes;

    // O(1) CPU-id to NumaNode lookup; sized to the highest CPU id seen.
    private readonly NumaNode[] _cpuToNode;

    // O(1) node-id to node-index lookup; sized to the highest node id seen.
    private readonly int[] _nodeIdToIndex;

    // Per-thread hint set when a thread is pinned to a specific node so that
    // GetCurrentNodeIndex() can skip the sched_getcpu() syscall on the hot path.
    [System.ThreadStatic]
    private static int t_pinnedNodeIndex;

    [System.ThreadStatic]
    private static bool t_hasNodeHint;

    /// <summary>Gets all detected NUMA nodes, ordered by node ID.</summary>
    public IReadOnlyList<NumaNode> Nodes => _nodes;

    /// <summary>Gets the number of NUMA nodes detected.</summary>
    public int NodeCount => _nodes.Length;

    /// <summary><c>true</c> when more than one NUMA node is active.</summary>
    public bool IsNumaSystem => NodeCount > 1;

    private NumaTopology()
    {
        _platform = NumaPlatformFactory.GetPlatform();
        IReadOnlyList<NumaAffinityInfo> infos = _platform.DiscoverNodes();

        _nodes = BuildNodes(infos);

        // Build O(1) CPU to node lookup.
        int maxCpuId = 0;
        foreach (NumaNode node in _nodes)
        {
            foreach (int cpu in node.CpuIds)
            {
                if (cpu > maxCpuId)
                {
                    maxCpuId = cpu;
                }
            }
        }

        _cpuToNode = new NumaNode[maxCpuId + 1];
        Array.Fill(_cpuToNode, _nodes[0]);
        foreach (NumaNode node in _nodes)
        {
            foreach (int cpu in node.CpuIds)
            {
                _cpuToNode[cpu] = node;
            }
        }

        // Build O(1) node-id to index lookup.
        int maxNodeId = 0;
        foreach (NumaNode node in _nodes)
        {
            if (node.NodeId > maxNodeId)
            {
                maxNodeId = node.NodeId;
            }
        }

        _nodeIdToIndex = new int[maxNodeId + 1];
        for (int i = 0; i < _nodes.Length; i++)
        {
            _nodeIdToIndex[_nodes[i].NodeId] = i;
        }
    }

    /// <summary>
    /// Hints that the calling thread is pinned to a specific NUMA node so that
    /// <see cref="GetCurrentNodeIndex"/> can skip the OS syscall on the hot path.
    /// Call this once after pinning the thread; clear with <see cref="ClearNodeIndexHint"/>.
    /// </summary>
    public static void SetCurrentNodeIndexHint(int nodeIndex)
    {
        t_pinnedNodeIndex = nodeIndex;
        t_hasNodeHint = true;
    }

    /// <summary>Clears the per-thread node-index hint set by <see cref="SetCurrentNodeIndexHint"/>.</summary>
    public static void ClearNodeIndexHint()
    {
        t_hasNodeHint = false;
        t_pinnedNodeIndex = 0;
    }

    /// <summary>
    /// Returns the index (into <see cref="Nodes"/>) of the NUMA node that owns the
    /// logical processor currently executing the calling thread. Returns 0 if unmappable.
    /// </summary>
    public int GetCurrentNodeIndex()
    {
        // Fast path: pinned reactor threads set this hint to avoid sched_getcpu().
        if (t_hasNodeHint)
        {
            return t_pinnedNodeIndex;
        }

        int nodeId = _platform.GetCurrentNodeId();
        if ((uint)nodeId < (uint)_nodeIdToIndex.Length)
        {
            return _nodeIdToIndex[nodeId];
        }

        return 0;
    }

    /// <summary>Returns the NUMA node that owns the given logical CPU ID. O(1) lookup.</summary>
    public NumaNode GetNodeForCpu(int cpuId)
    {
        if ((uint)cpuId < (uint)_cpuToNode.Length)
        {
            return _cpuToNode[cpuId];
        }

        return _nodes[0];
    }

    /// <summary>Returns the NUMA node ID that owns the given logical CPU ID. O(1) lookup.</summary>
    public int GetNodeIdForCpu(int cpuId)
    {
        if ((uint)cpuId < (uint)_cpuToNode.Length)
        {
            return _cpuToNode[cpuId].NodeId;
        }

        return _nodes[0].NodeId;
    }

    /// <summary>Returns the node with the specified ID.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Node ID not found.</exception>
    public NumaNode GetNode(int nodeId)
    {
        foreach (NumaNode node in _nodes)
        {
            if (node.NodeId == nodeId)
            {
                return node;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(nodeId), nodeId, "NUMA node not found.");
    }

    /// <summary>
    /// Pins the calling thread to all CPUs belonging to <paramref name="node"/>.
    /// Uses the active platform implementation; silently no-ops on unsupported platforms.
    /// </summary>
    public void PinCurrentThreadToNode(NumaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _platform.PinCurrentThreadToNode(node.AffinityInfo);
    }

    private static NumaNode[] BuildNodes(IReadOnlyList<NumaAffinityInfo> infos)
    {
        bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        NumaNode[] nodes = new NumaNode[infos.Count];
        for (int i = 0; i < infos.Count; i++)
        {
            long memBytes = isLinux ? ReadNodeMemoryBytes(infos[i].NodeId) : 0;
            nodes[i] = new NumaNode(infos[i], memBytes);
        }

        return nodes;
    }

    private static long ReadNodeMemoryBytes(int nodeId)
    {
        string path = $"/sys/devices/system/node/node{nodeId}/meminfo";
        if (!System.IO.File.Exists(path))
        {
            return 0;
        }

        foreach (string line in System.IO.File.ReadLines(path))
        {
            int idx = line.IndexOf("MemTotal:", StringComparison.Ordinal);
            if (idx < 0)
            {
                continue;
            }

            ReadOnlySpan<char> rest = line.AsSpan()[(idx + 9)..].Trim();
            int spaceIdx = rest.LastIndexOf(' ');
            if (spaceIdx >= 0 && long.TryParse(rest[..spaceIdx].Trim(), out long kb))
            {
                return kb * 1024L;
            }
        }

        return 0;
    }
}
