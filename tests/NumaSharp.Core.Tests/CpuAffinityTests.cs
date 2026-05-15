using System.Runtime.InteropServices;

namespace NumaSharp.Core.Tests;

public sealed class CpuAffinityTests
{
    [Test]
    public async Task SetCurrentThreadAffinity_DoesNotThrow_ForCpu0()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Affinity calls are no-ops on other platforms.
            return;
        }

        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinity(0))
            .ThrowsNothing();
    }

    [Test]
    public async Task SetCurrentThreadAffinity_ThrowsForNegativeCpu()
    {
        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinity(-1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task SetCurrentThreadAffinity_ThrowsForCpuBeyondProcessorCount()
    {
        int invalid = Environment.ProcessorCount + 1000;
        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinity(invalid))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task BuildNodeAffinityMask_ReturnsNonZeroForNodeWithCpus()
    {
        NumaTopology topology = NumaTopology.Instance;
        NumaNode node = topology.Nodes[0];

        ulong mask = CpuAffinity.BuildNodeAffinityMask(node);

        await Assert.That(mask).IsGreaterThan(0UL);
    }

    [Test]
    public async Task SetCurrentThreadAffinityMask_ThrowsForZeroMask()
    {
        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinityMask(0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task SetCurrentThreadAffinityMask_DoesNotThrow_ForValidMask()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        ulong mask = 1; // CPU 0
        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinityMask(mask))
            .ThrowsNothing();
    }

    [Test]
    public async Task SetCurrentThreadAffinityForCpus_DoesNotThrow_ForCpu0()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinityForCpus([0]))
            .ThrowsNothing();
    }

    [Test]
    public async Task SetCurrentThreadAffinityForCpus_NoOp_ForEmptyList()
    {
        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinityForCpus([]))
            .ThrowsNothing();
    }

    [Test]
    public async Task SetCurrentThreadAffinityForCpus_PinsToAllNodeCpus()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        NumaTopology topology = NumaTopology.Instance;
        IReadOnlyList<int> cpuIds = topology.Nodes[0].CpuIds;

        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinityForCpus(cpuIds))
            .ThrowsNothing();
    }

    [Test]
    public async Task SetCurrentThreadAffinityForCpus_ThrowsForNull()
    {
        await Assert.That(() => CpuAffinity.SetCurrentThreadAffinityForCpus(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task BuildNodeAffinityMask_ThrowsForNull()
    {
        await Assert.That(() => CpuAffinity.BuildNodeAffinityMask(null!))
            .Throws<ArgumentNullException>();
    }
}
