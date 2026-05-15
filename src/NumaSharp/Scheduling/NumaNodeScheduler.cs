using System.Collections.Concurrent;
using NumaSharp.Core;
using NumaSharp.Core.Platform;

namespace NumaSharp.Scheduling;

/// <summary>
/// A <see cref="TaskScheduler"/> that executes tasks exclusively on a dedicated
/// thread pool whose threads are pinned to a single NUMA node, keeping both
/// execution and (ideally) the data it touches local to that node's memory.
/// </summary>
public sealed class NumaNodeScheduler : TaskScheduler, IDisposable
{
    private readonly NumaNode                   _node;
    private readonly Thread[]                   _threads;
    private readonly BlockingCollection<Task>   _queue;
    private readonly CancellationTokenSource    _cts = new();
    private          int                        _disposed;

    /// <summary>The NUMA node this scheduler is pinned to.</summary>
    public NumaNode Node => _node;

    /// <summary>Number of worker threads in the pool.</summary>
    public int ThreadCount => _threads.Length;

    /// <summary>Approximate number of tasks waiting to be executed.</summary>
    public int PendingTaskCount => _queue.Count;

    /// <summary>
    /// Creates a new <see cref="NumaNodeScheduler"/> whose threads are pinned to <paramref name="node"/>.
    /// </summary>
    /// <param name="node">Target NUMA node.</param>
    /// <param name="threadCount">
    /// Worker-thread count. Defaults to the logical-processor count of <paramref name="node"/>.
    /// </param>
    public NumaNodeScheduler(NumaNode node, int? threadCount = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        int count = threadCount ?? node.ProcessorCount;
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threadCount), "Must be at least 1.");
        }

        _node    = node;
        _queue   = new BlockingCollection<Task>(new ConcurrentQueue<Task>());
        _threads = new Thread[count];

        for (int i = 0; i < count; i++)
        {
            Thread t = new(WorkerLoop)
            {
                IsBackground = true,
                Name         = $"NUMA-N{node.NodeId}-W{i}",
            };
            _threads[i] = t;
            t.Start();
        }
    }

    // ── Worker loop ───────────────────────────────────────────────────────────

    private void WorkerLoop()
    {
        PinCurrentThreadToNode(_node);

        // Cache the node index so NumaLocal<T>.Value and GetCurrentNodeIndex()
        // skip sched_getcpu() on every call while this thread is pinned.
        NumaTopology.SetCurrentNodeIndexHint(NumaTopology.Instance.GetCurrentNodeIndex());
        try
        {
            foreach (Task task in _queue.GetConsumingEnumerable(_cts.Token))
            {
                TryExecuteTask(task);
            }
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
        finally
        {
            NumaTopology.ClearNodeIndexHint();
        }
    }

    /// <summary>
    /// Pins the calling thread to <paramref name="node"/> via the active
    /// <see cref="INumaPlatform"/> implementation. Called internally and reused by
    /// <see cref="NumaTaskScheduler"/>.
    /// </summary>
    internal static void PinCurrentThreadToNode(NumaNode node) =>
        NumaTopology.Instance.PinCurrentThreadToNode(node);

    // ── TaskScheduler overrides ───────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void QueueTask(Task task)
    {
        ObjectDisposedException.ThrowIf(_disposed == 1, this);

        _queue.Add(task);
    }

    /// <inheritdoc/>
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        if (_disposed == 1)
        {
            return false;
        }

        // Inline only when the calling thread is already running on this NUMA node.
        return IsCallerOnThisNode() && TryExecuteTask(task);
    }

    /// <inheritdoc/>
    protected override IEnumerable<Task> GetScheduledTasks() => _queue.ToArray();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsCallerOnThisNode()
    {
        int nodeId = NumaTopology.Instance.GetCurrentNodeIndex();
        return nodeId >= 0 && NumaTopology.Instance.Nodes[nodeId].NodeId == _node.NodeId;
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Signals the worker threads to stop, waits up to 5 s for in-flight tasks,
    /// then releases resources.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        _cts.Cancel();
        _queue.CompleteAdding();
        foreach (Thread t in _threads)
        {
            t.Join(millisecondsTimeout: 5_000);
        }

        _queue.Dispose();
        _cts.Dispose();
    }
}
