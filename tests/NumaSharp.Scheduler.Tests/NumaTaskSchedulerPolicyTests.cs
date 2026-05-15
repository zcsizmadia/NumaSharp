using NumaSharp.Scheduling;

namespace NumaSharp.Scheduler.Tests;

/// <summary>Extended coverage for <see cref="NumaTaskScheduler"/> scheduling policies.</summary>
public sealed class NumaTaskSchedulerPolicyTests
{
    [Test]
    public async Task LeastLoaded_Policy_CompletesAllTasks()
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LeastLoaded);
        const int count = 100;
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
    public async Task LocalityFirst_Policy_CompletesAllTasks()
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LocalityFirst);
        const int count = 100;
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
    public async Task RoundRobin_Policy_CompletesAllTasks()
    {
        using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.RoundRobin);
        const int count = 100;
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
    public async Task RunOnNode_WithCancellation_PropagatesCancellation()
    {
        using NumaTaskScheduler scheduler = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.That(async () =>
        {
            await scheduler.RunOnNode(0, () => { }, cts.Token);
        }).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task RunOnNode_SyncFunc_ReturnsResult()
    {
        using NumaTaskScheduler scheduler = new();

        int result = await scheduler.RunOnNode(0, () => 99);

        await Assert.That(result).IsEqualTo(99);
    }

    [Test]
    public async Task RunOnNode_SyncAction_Completes()
    {
        using NumaTaskScheduler scheduler = new();
        bool ran = false;

        await scheduler.RunOnNode(0, () => { ran = true; });

        await Assert.That(ran).IsTrue();
    }

    [Test]
    public async Task Shared_IsNotNull()
    {
        await Assert.That(NumaTaskScheduler.Shared).IsNotNull();
    }

    [Test]
    public async Task Shared_NodeCount_IsPositive()
    {
        await Assert.That(NumaTaskScheduler.Shared.NodeCount).IsGreaterThan(0);
    }

    [Test]
    public async Task HighConcurrency_ManyTasks_AllComplete()
    {
        using NumaTaskScheduler scheduler = new();
        const int count = 1000;
        int completed = 0;
        Task[] tasks = new Task[count];

        for (int i = 0; i < count; i++)
        {
            int nodeIndex = i % scheduler.NodeCount;
            tasks[i] = scheduler.RunOnNode(nodeIndex,
                () => Interlocked.Increment(ref completed));
        }

        await Task.WhenAll(tasks);
        await Assert.That(completed).IsEqualTo(count);
    }

    [Test]
    public async Task GetNodeStats_AllNodesHaveNonNegativePendingCount()
    {
        using NumaTaskScheduler scheduler = new();
        IReadOnlyList<(int NodeId, int PendingTasks)> stats = scheduler.GetNodeStats();

        foreach ((int _, int pending) in stats)
        {
            await Assert.That(pending).IsGreaterThanOrEqualTo(0);
        }
    }
}
