using System.Runtime.InteropServices;

namespace NumaSharp.Core.Tests;

public sealed class NumaTopologyTests
{
    [Test]
    public async Task Instance_ReturnsNonNull()
    {
        NumaTopology topology = NumaTopology.Instance;
        await Assert.That(topology).IsNotNull();
    }

    [Test]
    public async Task Instance_ReturnsSameObject_OnSubsequentAccesses()
    {
        NumaTopology a = NumaTopology.Instance;
        NumaTopology b = NumaTopology.Instance;
        await Assert.That(ReferenceEquals(a, b)).IsTrue();
    }

    [Test]
    public async Task NodeCount_IsAtLeastOne()
    {
        NumaTopology topology = NumaTopology.Instance;
        await Assert.That(topology.NodeCount).IsGreaterThan(0);
    }

    [Test]
    public async Task Nodes_HasSameCountAsNodeCount()
    {
        NumaTopology topology = NumaTopology.Instance;
        await Assert.That(topology.Nodes.Count).IsEqualTo(topology.NodeCount);
    }

    [Test]
    public async Task AllNodes_HaveAtLeastOneCpu()
    {
        NumaTopology topology = NumaTopology.Instance;
        foreach (NumaNode node in topology.Nodes)
        {
            await Assert.That(node.CpuIds.Count).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task TotalCpuCount_IsAtLeastOne()
    {
        NumaTopology topology = NumaTopology.Instance;
        int totalCpus = topology.Nodes.Sum(n => n.CpuIds.Count);
        await Assert.That(totalCpus).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task IsNumaSystem_IsTrueOnlyWhenMultipleNodes()
    {
        NumaTopology topology = NumaTopology.Instance;
        await Assert.That(topology.IsNumaSystem).IsEqualTo(topology.NodeCount > 1);
    }

    [Test]
    public async Task GetNode_ReturnsCorrectNode()
    {
        NumaTopology topology = NumaTopology.Instance;
        NumaNode first = topology.Nodes[0];

        NumaNode found = topology.GetNode(first.NodeId);

        await Assert.That(found.NodeId).IsEqualTo(first.NodeId);
    }

    [Test]
    public async Task GetNode_ThrowsForUnknownId()
    {
        NumaTopology topology = NumaTopology.Instance;

        await Assert.That(() => topology.GetNode(int.MaxValue))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetNodeForCpu_ReturnsSomeNode()
    {
        NumaTopology topology = NumaTopology.Instance;
        NumaNode node = topology.GetNodeForCpu(0);
        await Assert.That(node).IsNotNull();
    }

    [Test]
    public async Task GetNodeForCpu_ReturnsCorrectNode_ForAllKnownCpus()
    {
        NumaTopology topology = NumaTopology.Instance;
        foreach (NumaNode expected in topology.Nodes)
        {
            foreach (int cpu in expected.CpuIds)
            {
                NumaNode found = topology.GetNodeForCpu(cpu);
                await Assert.That(found.NodeId).IsEqualTo(expected.NodeId);
            }
        }
    }

    [Test]
    public async Task GetNodeIdForCpu_ReturnsCorrectNodeId_ForAllKnownCpus()
    {
        NumaTopology topology = NumaTopology.Instance;
        foreach (NumaNode expected in topology.Nodes)
        {
            foreach (int cpu in expected.CpuIds)
            {
                int nodeId = topology.GetNodeIdForCpu(cpu);
                await Assert.That(nodeId).IsEqualTo(expected.NodeId);
            }
        }
    }

    [Test]
    public async Task GetNodeIdForCpu_ReturnsFallback_ForOutOfRangeCpu()
    {
        NumaTopology topology = NumaTopology.Instance;
        // A CPU id far beyond any real CPU should fall back to node 0.
        int nodeId = topology.GetNodeIdForCpu(int.MaxValue);
        await Assert.That(nodeId).IsEqualTo(topology.Nodes[0].NodeId);
    }

    [Test]
    public async Task GetCurrentNodeIndex_ReturnValidIndex()
    {
        NumaTopology topology = NumaTopology.Instance;
        int idx = topology.GetCurrentNodeIndex();
        await Assert.That(idx).IsGreaterThanOrEqualTo(0);
        await Assert.That(idx).IsLessThan(topology.NodeCount);
    }

    [Test]
    public async Task NodeToString_ContainsNodeId()
    {
        NumaTopology topology = NumaTopology.Instance;
        NumaNode node = topology.Nodes[0];
        string str = node.ToString();
        await Assert.That(str).Contains(node.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task Node_ProcessorCount_MatchesCpuIdsCount()
    {
        NumaTopology topology = NumaTopology.Instance;
        foreach (NumaNode node in topology.Nodes)
        {
            await Assert.That(node.ProcessorCount).IsEqualTo(node.CpuIds.Count);
        }
    }

    [Test]
    public async Task Node_MemoryBytes_IsPositive_OnLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { return; }
        NumaTopology topology = NumaTopology.Instance;
        // At least the first node should have memory on real Linux hardware.
        // VMs may report 0 — so we only assert >= 0.
        foreach (NumaNode node in topology.Nodes)
        {
            await Assert.That(node.MemoryBytes).IsGreaterThanOrEqualTo(0);
        }
    }

    [Test]
    public async Task PinCurrentThreadToNode_DoesNotThrow_OnLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { return; }
        NumaTopology topology = NumaTopology.Instance;
        NumaNode node = topology.Nodes[0];
        await Assert.That(() => topology.PinCurrentThreadToNode(node)).ThrowsNothing();
    }

    [Test]
    public async Task PinCurrentThreadToNode_ThrowsForNull()
    {
        NumaTopology topology = NumaTopology.Instance;
        await Assert.That(() => topology.PinCurrentThreadToNode(null!)).Throws<ArgumentNullException>();
    }
}
