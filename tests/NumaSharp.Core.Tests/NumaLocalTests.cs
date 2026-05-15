using System.Collections.Concurrent;

namespace NumaSharp.Core.Tests;

public sealed class NumaLocalTests
{
    // ── construction ──────────────────────────────────────────────────────────

    [Test]
    public async Task Constructor_NodeFactory_CreatesOneValuePerNode()
    {
        using NumaLocal<int[]> local = new(node => new int[] { node.NodeId });
        await Assert.That(local.NodeCount).IsEqualTo(NumaTopology.Instance.NodeCount);
    }

    [Test]
    public async Task Constructor_NodeFactory_NullFactory_Throws()
    {
        Func<NumaNode, string> factory = null!;
        await Assert.That(() => new NumaLocal<string>(factory))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Constructor_EachNodeReceivesItsOwnNodeId()
    {
        using NumaLocal<int> local = new(node => node.NodeId);
        for (int i = 0; i < local.NodeCount; i++)
        {
            int expected = NumaTopology.Instance.Nodes[i].NodeId;
            await Assert.That(local.GetForNode(i)).IsEqualTo(expected);
        }
    }

    // ── Value ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Value_ReturnsNonDefault()
    {
        using NumaLocal<string> local = new((NumaNode _) => "hello");
        await Assert.That(local.Value).IsEqualTo("hello");
    }

    [Test]
    public async Task Value_IsWithinNodeRange()
    {
        using NumaLocal<int> local = new(node => node.NodeId);
        int v = local.Value;
        int nodeCount = NumaTopology.Instance.NodeCount;
        await Assert.That(v).IsGreaterThanOrEqualTo(0);
        await Assert.That(v).IsLessThan(nodeCount);
    }

    [Test]
    public async Task Value_ReturnsSameReferenceOnSingleNode()
    {
        object obj = new();
        using NumaLocal<object> local = new((NumaNode _) => obj);
        await Assert.That(ReferenceEquals(local.Value, obj)).IsTrue();
    }

    // ── GetForNode ────────────────────────────────────────────────────────────

    [Test]
    public async Task GetForNode_ValidIndex_ReturnsCorrectValue()
    {
        using NumaLocal<int> local = new(node => node.NodeId * 10);
        for (int i = 0; i < local.NodeCount; i++)
        {
            await Assert.That(local.GetForNode(i)).IsEqualTo(i * 10);
        }
    }

    [Test]
    public async Task GetForNode_NegativeIndex_Throws()
    {
        using NumaLocal<int> local = new((NumaNode _) => 0);
        await Assert.That(() => local.GetForNode(-1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task GetForNode_IndexEqualToNodeCount_Throws()
    {
        using NumaLocal<int> local = new((NumaNode _) => 0);
        await Assert.That(() => local.GetForNode(local.NodeCount))
            .Throws<ArgumentOutOfRangeException>();
    }

    // ── Aggregate (selector + accumulator) ───────────────────────────────────

    [Test]
    public async Task Aggregate_SumOfNodeIds_IsCorrect()
    {
        using NumaLocal<int> local = new(node => node.NodeId);
        int expected = NumaTopology.Instance.Nodes
            .Select(static n => n.NodeId)
            .Sum();
        int actual = local.Aggregate(static x => x, static (a, b) => a + b, 0);
        await Assert.That(actual).IsEqualTo(expected);
    }

    [Test]
    public async Task Aggregate_NullSelector_Throws()
    {
        using NumaLocal<int> local = new((NumaNode _) => 1);
        Func<int, int> selector = null!;
        await Assert.That(() => local.Aggregate(selector, static (a, b) => a + b))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task Aggregate_NullAccumulator_Throws()
    {
        using NumaLocal<int> local = new((NumaNode _) => 1);
        Func<int, int, int> acc = null!;
        await Assert.That(() => local.Aggregate(static x => x, acc))
            .Throws<ArgumentNullException>();
    }

    // ── Aggregate (accumulator only) ──────────────────────────────────────────

    [Test]
    public async Task Aggregate_Direct_SumIsCorrect()
    {
        using NumaLocal<long> local = new((NumaNode _) => 5L);
        long result = local.Aggregate(static (a, b) => a + b, 0L);
        await Assert.That(result).IsEqualTo(5L * local.NodeCount);
    }

    [Test]
    public async Task Aggregate_Direct_NullAccumulator_Throws()
    {
        using NumaLocal<long> local = new((NumaNode _) => 1L);
        Func<long, long, long> acc = null!;
        await Assert.That(() => local.Aggregate(acc))
            .Throws<ArgumentNullException>();
    }

    // ── ForEach ───────────────────────────────────────────────────────────────

    [Test]
    public async Task ForEach_VisitsAllNodes()
    {
        using NumaLocal<int> local = new(node => node.NodeId);
        List<(int NodeId, int Value)> seen = [];
        local.ForEach((nodeId, value) => seen.Add((nodeId, value)));
        await Assert.That(seen.Count).IsEqualTo(local.NodeCount);
        for (int i = 0; i < local.NodeCount; i++)
        {
            await Assert.That(seen[i].NodeId).IsEqualTo(i);
            await Assert.That(seen[i].Value).IsEqualTo(i);
        }
    }

    [Test]
    public async Task ForEach_NullAction_Throws()
    {
        using NumaLocal<int> local = new((NumaNode _) => 0);
        Action<int, int> action = null!;
        await Assert.That(() => local.ForEach(action))
            .Throws<ArgumentNullException>();
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Dispose_DisposesIDisposableValues()
    {
        int disposeCount = 0;
        DisposableSpy[] spies = new DisposableSpy[NumaTopology.Instance.NodeCount];
        for (int i = 0; i < spies.Length; i++)
        {
            spies[i] = new DisposableSpy(() => Interlocked.Increment(ref disposeCount));
        }

        int idx = 0;
        NumaLocal<DisposableSpy> local = new(_ => spies[idx++]);
        local.Dispose();

        await Assert.That(disposeCount).IsEqualTo(NumaTopology.Instance.NodeCount);
    }

    [Test]
    public async Task Dispose_CalledTwice_DisposesOnce()
    {
        int disposeCount = 0;
        NumaLocal<DisposableSpy> local = new(
            _ => new DisposableSpy(() => Interlocked.Increment(ref disposeCount)));
        local.Dispose();
        local.Dispose();
        await Assert.That(disposeCount).IsEqualTo(NumaTopology.Instance.NodeCount);
    }

    [Test]
    public async Task Dispose_WithCustomDisposeAction_InvokesAction()
    {
        int count = 0;
        NumaLocal<int[]> local = new(
            (NumaNode _) => new int[4],
            dispose: _ => Interlocked.Increment(ref count));
        local.Dispose();
        await Assert.That(count).IsEqualTo(NumaTopology.Instance.NodeCount);
    }

    // ── Concurrency ───────────────────────────────────────────────────────────

    [Test]
    public async Task Value_ConcurrentAccess_DoesNotThrow()
    {
        using NumaLocal<ConcurrentBag<int>> local = new((NumaNode _) => new ConcurrentBag<int>());
        int threadCount = Math.Min(Environment.ProcessorCount, 8);

        Task[] tasks = new Task[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < 1_000; j++)
                {
                    local.Value.Add(j);
                }
            });
        }

        await Task.WhenAll(tasks);

        int total = 0;
        local.ForEach((_, bag) => total += bag.Count);
        await Assert.That(total).IsGreaterThan(0);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private sealed class DisposableSpy(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
