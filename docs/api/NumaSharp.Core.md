# NumaSharp.Core API Reference

## Namespace: `NumaSharp.Core`

Provides NUMA topology detection, CPU affinity control, and a NUMA-aware native memory pool. These primitives are the foundation on which the rest of NumaSharp is built.

---

## `NumaTopology`

Singleton that exposes the hardware NUMA layout of the current machine.

```csharp
public sealed class NumaTopology
```

### Properties

| Member | Type | Description |
|--------|------|-------------|
| `Instance` | `NumaTopology` | The singleton instance (lazy-initialized, thread-safe). |
| `NodeCount` | `int` | Number of NUMA nodes detected. Always ≥ 1. |
| `Nodes` | `IReadOnlyList<NumaNode>` | Ordered list of NUMA nodes (index equals `NodeId`). |

### Example

```csharp
NumaTopology topology = NumaTopology.Instance;
Console.WriteLine($"{topology.NodeCount} NUMA nodes detected.");
foreach (NumaNode node in topology.Nodes)
{
    Console.WriteLine($"  Node {node.NodeId}: {node.ProcessorCount} CPUs, {node.MemoryBytes / (1024 * 1024)} MB");
}
```

---

## `NumaNode`

Describes a single NUMA node — its logical CPU set and total memory.

```csharp
public sealed class NumaNode
```

### Properties

| Member | Type | Description |
|--------|------|-------------|
| `NodeId` | `int` | Zero-based node index. |
| `ProcessorCount` | `int` | Number of logical CPUs belonging to this node. |
| `CpuIds` | `IReadOnlyList<int>` | Logical CPU numbers assigned to this node. |
| `MemoryBytes` | `long` | Total physical memory attached to this node (bytes). 0 on non-Linux. |

### Notes

- `CpuIds` are globally unique across nodes.
- `NodeId` values are sequential starting from 0 (matches the list index in `NumaTopology.Nodes`).

---

## `NumaMemoryPool`

A `MemoryPool<byte>` backed by native, NUMA-local memory allocations. Blocks are
64-byte aligned via `NativeMemory.AlignedAlloc`.

```csharp
public sealed class NumaMemoryPool : MemoryPool<byte>
```

### Constructor

```csharp
public NumaMemoryPool(int numaNodeId = 0, int blockSize = 4096)
```

| Parameter | Description |
|-----------|-------------|
| `numaNodeId` | NUMA node to allocate memory on. Must be ≥ 0. |
| `blockSize` | Block size in bytes. Must be a power of two and ≥ 64. Default: 4096. |

**Throws** `ArgumentOutOfRangeException` if `numaNodeId < 0` or `blockSize < 64`.  
**Throws** `ArgumentException` if `blockSize` is not a power of two.

### Properties

| Member | Type | Description |
|--------|------|-------------|
| `NumaNodeId` | `int` | The NUMA node this pool allocates from. |
| `BlockSize` | `int` | Block size passed at construction. |
| `MaxBufferSize` | `int` | Always `int.MaxValue`. |

### Methods

| Method | Description |
|--------|-------------|
| `Rent(int minBufferSize = -1)` | Returns an `IMemoryOwner<byte>` whose `Memory` is at least `max(blockSize, minBufferSize)` bytes. |
| `Dispose()` | Frees all pooled native blocks. |

### Example

```csharp
using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
using IMemoryOwner<byte> buf = pool.Rent();
buf.Memory.Span.Fill(0);
// Use buf.Memory.Span for zero-copy I/O...
```

---

## `CpuAffinity`

Static helpers for pinning threads to specific logical CPUs or NUMA nodes. Linux-only; no-ops or throws `PlatformNotSupportedException` on other platforms.

```csharp
public static class CpuAffinity
```

### Methods

| Method | Description |
|--------|-------------|
| `SetCurrentThreadAffinity(int cpuId)` | Pins the calling thread to a single logical CPU. |
| `SetCurrentThreadAffinityMask(ulong mask)` | Sets a raw CPU affinity bitmask on the calling thread. |
| `SetCurrentThreadAffinityForCpus(IReadOnlyList<int> cpuIds)` | Pins the calling thread to a set of CPUs (builds a mask and calls `sched_setaffinity`). |
| `BuildNodeAffinityMask(NumaNode node)` | Returns a `ulong` bitmask with bits set for every CPU in the given node. |

### Example

```csharp
NumaNode node = NumaTopology.Instance.Nodes[0];
CpuAffinity.SetCurrentThreadAffinityForCpus(node.CpuIds);
// Current thread is now pinned to node 0's CPUs.
```
