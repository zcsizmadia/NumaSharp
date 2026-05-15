using System.Runtime.InteropServices;
using NumaSharp.Core;
using NumaSharp.Scheduling;

namespace NumaSharp.Scheduler.Tests;

/// <summary>Additional coverage for <see cref="NumaNodeScheduler"/>.</summary>
public sealed class NumaNodeSchedulerExtendedTests
{
    [Test]
    public async Task Constructor_WithExplicitThreadCount_UsesCorrectThreadCount()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node, threadCount: 2);
        await Assert.That(scheduler.ThreadCount).IsEqualTo(2);
    }

    [Test]
    public async Task Constructor_DefaultThreadCount_MatchesNodeProcessorCount()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node);
        await Assert.That(scheduler.ThreadCount).IsEqualTo(node.ProcessorCount);
    }

    [Test]
    public async Task Node_ReturnsConstructorArgument()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node, threadCount: 1);
        await Assert.That(ReferenceEquals(scheduler.Node, node)).IsTrue();
    }

    [Test]
    public async Task Constructor_NullNode_Throws()
    {
        await Assert.That(() => new NumaNodeScheduler(null!, threadCount: 1))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_ZeroThreadCount_Throws()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        await Assert.That(() => new NumaNodeScheduler(node, threadCount: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Constructor_NegativeThreadCount_Throws()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        await Assert.That(() => new NumaNodeScheduler(node, threadCount: -1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task PendingTaskCount_IsNonNegative_WhenIdle()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node, threadCount: 1);
        await Assert.That(scheduler.PendingTaskCount).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task ScheduleTask_Executes_OnScheduler()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node, threadCount: 1);

        bool ran = false;
        Task task = Task.Factory.StartNew(
            () => { ran = true; },
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);

        await task;
        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task ScheduleTask_PropagatesReturnValue()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node, threadCount: 1);

        Task<int> task = Task.Factory.StartNew(
            () => 42,
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);

        int result = await task;
        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task ScheduleTask_PropagatesException()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node, threadCount: 1);

        Task task = Task.Factory.StartNew(
            () => throw new InvalidOperationException("test"),
            CancellationToken.None,
            TaskCreationOptions.None,
            scheduler);

        await Assert.That(() => task).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ConcurrentTasks_AllComplete()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node, threadCount: 4);

        const int count = 200;
        int completed = 0;
        Task[] tasks = new Task[count];

        for (int i = 0; i < count; i++)
        {
            tasks[i] = Task.Factory.StartNew(
                () => Interlocked.Increment(ref completed),
                CancellationToken.None,
                TaskCreationOptions.None,
                scheduler);
        }

        await Task.WhenAll(tasks);
        await Assert.That(completed).IsEqualTo(count);
    }

    [Test]
    public async Task Dispose_StopsWorkers_GracefullyWithoutHang()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        NumaNodeScheduler scheduler = new(node, threadCount: 2);

        await Assert.That(() => scheduler.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task PinCurrentThreadToNode_OnLinux_DoesNotThrow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        NumaNode node = NumaTopology.Instance.Nodes[0];
        await Assert.That(() => NumaNodeScheduler.PinCurrentThreadToNode(node))
            .ThrowsNothing();
    }
}
