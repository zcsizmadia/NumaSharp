# NumaSharp.Scheduling API Reference

## Namespace: `NumaSharp.Scheduling`

NUMA-aware task scheduling: distribute work across NUMA nodes so that every task runs on CPU cores local to the memory it touches. No cross-NUMA memory traffic on hot paths.

---

## `NumaTaskScheduler`

A `TaskScheduler` that manages one worker group per NUMA node and routes tasks to the appropriate group based on a scheduling policy.

```csharp
public sealed class NumaTaskScheduler : TaskScheduler, IDisposable
```

### Constructor

```csharp
public NumaTaskScheduler(NumaSchedulingPolicy policy = NumaSchedulingPolicy.LocalityFirst)
```

### Static Properties

| Member | Type | Description |
|--------|------|-------------|
| `Shared` | `NumaTaskScheduler` | Shared singleton using `LocalityFirst` policy. Do not dispose. |

### Properties

| Member | Type | Description |
|--------|------|-------------|
| `NodeCount` | `int` | Number of NUMA nodes this scheduler manages. |
| `Topology` | `NumaTopology` | The detected NUMA topology. |

### Methods

| Method | Description |
|--------|-------------|
| `RunOnNode(int nodeIndex, Action action, CancellationToken ct = default)` | Schedules `action` to run on the specified NUMA node's worker group. Returns a `Task`. |
| `RunOnNode(int nodeIndex, Func<T> func, CancellationToken ct = default)` | Schedules `func` and returns `Task<T>`. |
| `GetNodeStats()` | Returns `IReadOnlyList<(int NodeId, int PendingTasks)>` — a snapshot of pending work per node. |
| `Dispose()` | Stops all worker threads and releases resources. |

### Example

```csharp
using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LeastLoaded);

// Run work pinned to node 0
await scheduler.RunOnNode(0, () =>
{
    // This runs on a thread pinned to node 0's CPUs
    ProcessNodeLocalData();
});
```

---

## `NumaNodeScheduler`

A single-node `TaskScheduler` backed by a fixed thread pool, all threads pinned to a specific NUMA node's CPUs.

```csharp
public sealed class NumaNodeScheduler : TaskScheduler, IDisposable
```

### Constructor

```csharp
public NumaNodeScheduler(NumaNode node, int? threadCount = null)
```

| Parameter | Description |
|-----------|-------------|
| `node` | The NUMA node to pin workers to. |
| `threadCount` | Number of worker threads. Defaults to `node.ProcessorCount`. |

### Properties

| Member | Type | Description |
|--------|------|-------------|
| `Node` | `NumaNode` | The NUMA node this scheduler is pinned to. |
| `ThreadCount` | `int` | Number of worker threads. |
| `PendingTaskCount` | `int` | Approximate number of queued tasks. |

### Static Methods

| Method | Description |
|--------|-------------|
| `PinCurrentThreadToNode(NumaNode node)` | Sets the affinity of the calling thread to all CPUs in `node`. |

---

## `NumaTaskExtensions`

Extension methods for scheduling delegates on NUMA nodes via `NumaTaskScheduler.Shared`.

```csharp
public static class NumaTaskExtensions
```

### Methods

| Method | Description |
|--------|-------------|
| `RunOnNumaNode(this Action action, int nodeIndex, CancellationToken ct = default)` | Schedules the action on the given node index. |
| `RunOnNumaNode<T>(this Func<T> func, int nodeIndex, CancellationToken ct = default)` | Schedules and returns the result. |
| `RunNumaLocal(this Action action, CancellationToken ct = default)` | Schedules on the NUMA node local to the calling thread (LocalityFirst policy). |

### Example

```csharp
// Schedule work on node 1 using the shared scheduler
await ((Action)DoWork).RunOnNumaNode(nodeIndex: 1);

// Schedule on the caller's local NUMA node
await ((Action)UpdateLocalCache).RunNumaLocal();
```

---

## `NumaSchedulingPolicy`

Enum controlling how `NumaTaskScheduler` picks a node when no explicit node index is given (e.g. when used as a plain `TaskScheduler`).

| Value | Description |
|-------|-------------|
| `RoundRobin` | Tasks cycle through nodes 0, 1, 2, … in order. |
| `LocalityFirst` | Prefers the NUMA node of the calling thread. Falls back to round-robin. |
| `LeastLoaded` | Routes to the node with the fewest pending tasks. |
