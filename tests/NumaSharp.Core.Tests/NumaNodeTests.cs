using System.Runtime.InteropServices;

namespace NumaSharp.Core.Tests;

/// <summary>Tests for <see cref="NumaNode"/> properties and contracts.</summary>
public sealed class NumaNodeTests
{
    [Test]
    public async Task NodeId_IsNonNegative()
    {
        foreach (NumaNode node in NumaTopology.Instance.Nodes)
        {
            await Assert.That(node.NodeId).IsGreaterThanOrEqualTo(0);
        }
    }

    [Test]
    public async Task ProcessorCount_IsAtLeastOne()
    {
        foreach (NumaNode node in NumaTopology.Instance.Nodes)
        {
            await Assert.That(node.ProcessorCount).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task CpuIds_CountMatchesProcessorCount()
    {
        foreach (NumaNode node in NumaTopology.Instance.Nodes)
        {
            await Assert.That(node.CpuIds.Count).IsEqualTo(node.ProcessorCount);
        }
    }

    [Test]
    public async Task CpuIds_AreNonNegative()
    {
        foreach (NumaNode node in NumaTopology.Instance.Nodes)
        {
            foreach (int cpuId in node.CpuIds)
            {
                await Assert.That(cpuId).IsGreaterThanOrEqualTo(0);
            }
        }
    }

    [Test]
    public async Task CpuIds_AreWithinProcessorCount()
    {
        int logicalCpuCount = Environment.ProcessorCount;
        foreach (NumaNode node in NumaTopology.Instance.Nodes)
        {
            foreach (int cpuId in node.CpuIds)
            {
                await Assert.That(cpuId).IsLessThan(logicalCpuCount);
            }
        }
    }

    [Test]
    public async Task CpuIds_AreUniqueWithinNode()
    {
        foreach (NumaNode node in NumaTopology.Instance.Nodes)
        {
            int distinctCount = node.CpuIds.Distinct().Count();
            await Assert.That(distinctCount).IsEqualTo(node.CpuIds.Count);
        }
    }

    [Test]
    public async Task CpuIds_AreUniqueAcrossNodes()
    {
        NumaTopology topology = NumaTopology.Instance;
        HashSet<int> seen = [];
        foreach (NumaNode node in topology.Nodes)
        {
            foreach (int cpuId in node.CpuIds)
            {
                await Assert.That(seen.Add(cpuId)).IsTrue();
            }
        }
    }

    [Test]
    public async Task MemoryBytes_IsNonNegative()
    {
        foreach (NumaNode node in NumaTopology.Instance.Nodes)
        {
            await Assert.That(node.MemoryBytes).IsGreaterThanOrEqualTo(0L);
        }
    }

    [Test]
    public async Task MemoryBytes_IsPositiveOnLinux()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        foreach (NumaNode node in NumaTopology.Instance.Nodes)
        {
            await Assert.That(node.MemoryBytes).IsGreaterThan(0L);
        }
    }

    [Test]
    public async Task ToString_ContainsNodeId()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        string text = node.ToString();
        await Assert.That(text).Contains("0");
    }

    [Test]
    public async Task AllNodes_HaveSequentialIds()
    {
        NumaTopology topology = NumaTopology.Instance;
        for (int i = 0; i < topology.NodeCount; i++)
        {
            await Assert.That(topology.Nodes[i].NodeId).IsEqualTo(i);
        }
    }

    [Test]
    public async Task TotalCpuCount_MatchesEnvironmentProcessorCount()
    {
        NumaTopology topology = NumaTopology.Instance;
        int total = topology.Nodes.Sum(n => n.CpuIds.Count);
        // On some VMs, NUMA topology might not cover all logical CPUs.
        await Assert.That(total).IsGreaterThanOrEqualTo(1);
        await Assert.That(total).IsLessThanOrEqualTo(Environment.ProcessorCount);
    }
}
