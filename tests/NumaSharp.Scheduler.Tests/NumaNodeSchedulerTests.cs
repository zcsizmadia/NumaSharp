using NumaSharp.Core;
using NumaSharp.Scheduling;

namespace NumaSharp.Scheduler.Tests;

public sealed class NumaNodeSchedulerTests
{
    [Test]
    public async Task Constructor_SetsNodeProperty()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node);
        await Assert.That(scheduler.Node).IsEqualTo(node);
    }

    [Test]
    public async Task Constructor_ThrowsForNull()
    {
        await Assert.That(() => new NumaNodeScheduler(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ThreadCount_IsAtLeastOne()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node);
        await Assert.That(scheduler.ThreadCount).IsGreaterThan(0);
    }

    [Test]
    public async Task ThreadCount_CanBeOverridden()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node, threadCount: 2);
        await Assert.That(scheduler.ThreadCount).IsEqualTo(2);
    }

    [Test]
    public async Task PendingTaskCount_IsZeroInitially()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node);
        await Assert.That(scheduler.PendingTaskCount).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Schedule_ExecutesTask()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node);

        bool ran = false;
        Task task = Task.Factory.StartNew(() => { ran = true; }, CancellationToken.None,
            TaskCreationOptions.None, scheduler);

        await task;

        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task Schedule_MultipleTasksComplete()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        using NumaNodeScheduler scheduler = new(node);

        int count = 0;
        Task[] tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Factory.StartNew(() => Interlocked.Increment(ref count),
                CancellationToken.None, TaskCreationOptions.None, scheduler))
            .ToArray();

        await Task.WhenAll(tasks);

        await Assert.That(count).IsEqualTo(20);
    }

    [Test]
    public async Task Dispose_DoesNotThrow()
    {
        NumaNode node = NumaTopology.Instance.Nodes[0];
        NumaNodeScheduler scheduler = new(node);
        await Assert.That(() => scheduler.Dispose()).ThrowsNothing();
    }
}
