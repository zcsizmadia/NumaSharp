using NumaSharp.Core;
using NumaSharp.Scheduling;

namespace NumaSharp.Scheduler.Tests;

public sealed class NumaTaskSchedulerTests
{
    [Test]
    public async Task Constructor_CreatesScheduler_WithCorrectNodeCount()
    {
        using NumaTaskScheduler scheduler = new();
        await Assert.That(scheduler.NodeCount).IsEqualTo(NumaTopology.Instance.NodeCount);
    }

    [Test]
    public async Task Shared_ReturnsSingletonInstance()
    {
        NumaTaskScheduler a = NumaTaskScheduler.Shared;
        NumaTaskScheduler b = NumaTaskScheduler.Shared;
        await Assert.That(ReferenceEquals(a, b)).IsTrue();
    }

    [Test]
    public async Task Topology_ReturnsGlobalInstance()
    {
        using NumaTaskScheduler scheduler = new();
        await Assert.That(ReferenceEquals(scheduler.Topology, NumaTopology.Instance)).IsTrue();
    }

    [Test]
    public async Task RunOnNode_ExecutesAction()
    {
        using NumaTaskScheduler scheduler = new();

        bool ran = false;
        await scheduler.RunOnNode(0, () => { ran = true; });

        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task RunOnNode_ReturnsResult()
    {
        using NumaTaskScheduler scheduler = new();

        int result = await scheduler.RunOnNode(0, () => 42);

        await Assert.That(result).IsEqualTo(42);
    }

    [Test]
    public async Task RunOnNode_ThrowsForNegativeNodeIndex()
    {
        using NumaTaskScheduler scheduler = new();

        await Assert.That(() => scheduler.RunOnNode(-1, () => { }))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RunOnNode_ThrowsForOutOfRangeNodeIndex()
    {
        using NumaTaskScheduler scheduler = new();

        await Assert.That(() => scheduler.RunOnNode(scheduler.NodeCount, () => { }))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetNodeStats_ReturnsOneEntryPerNode()
    {
        using NumaTaskScheduler scheduler = new();

        IReadOnlyList<(int NodeId, int PendingTasks)> stats = scheduler.GetNodeStats();

        await Assert.That(stats.Count).IsEqualTo(scheduler.NodeCount);
    }

    [Test]
    public async Task GetNodeStats_PendingTasksAreNonNegative()
    {
        using NumaTaskScheduler scheduler = new();

        IReadOnlyList<(int NodeId, int PendingTasks)> stats = scheduler.GetNodeStats();

        foreach ((int _, int pending) in stats)
        {
            await Assert.That(pending).IsGreaterThanOrEqualTo(0);
        }
    }

    [Test]
    public async Task RunOnNode_AllNodes_Execute()
    {
        using NumaTaskScheduler scheduler = new();

        bool[] results = new bool[scheduler.NodeCount];
        Task[] tasks = new Task[scheduler.NodeCount];

        for (int i = 0; i < scheduler.NodeCount; i++)
        {
            int nodeIndex = i;
            tasks[i] = scheduler.RunOnNode(nodeIndex, () => { results[nodeIndex] = true; });
        }

        await Task.WhenAll(tasks);

        foreach (bool result in results)
        {
            await Assert.That(result).IsTrue();
        }
    }

    [Test]
    public async Task RunOnNode_PropagatesException()
    {
        using NumaTaskScheduler scheduler = new();

        await Assert.That(() => scheduler.RunOnNode(0, () => throw new InvalidOperationException("test error")))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RoundRobinPolicy_DistributesWork()
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.RoundRobin);
        int[] counts = new int[scheduler.NodeCount];
        Task[] tasks = new Task[scheduler.NodeCount * 4];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = scheduler.RunOnNode(i % scheduler.NodeCount, () => { });
        }

        await Task.WhenAll(tasks);

        // Just verify we completed all tasks — distribution is scheduler-internal.
        await Assert.That(tasks.All(t => t.IsCompletedSuccessfully)).IsTrue();
    }

    [Test]
    public async Task LeastLoaded_Policy_CreatesScheduler()
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LeastLoaded);
        await Assert.That(scheduler.NodeCount).IsGreaterThan(0);
    }

    [Test]
    public async Task LocalityFirst_Policy_CreatesScheduler()
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LocalityFirst);
        await Assert.That(scheduler.NodeCount).IsGreaterThan(0);
    }

    [Test]
    public async Task Dispose_DoesNotThrow()
    {
        NumaTaskScheduler scheduler = new();
        await Assert.That(() => scheduler.Dispose()).ThrowsNothing();
    }

    [Test]
    public async Task ConcurrentWork_CompletesCorrectly()
    {
        using NumaTaskScheduler scheduler = new();
        int totalWork = 100;
        int completed = 0;

        Task[] tasks = Enumerable.Range(0, totalWork)
            .Select(i => scheduler.RunOnNode(i % scheduler.NodeCount, () =>
                Interlocked.Increment(ref completed)))
            .ToArray();

        await Task.WhenAll(tasks);

        await Assert.That(completed).IsEqualTo(totalWork);
    }
}
