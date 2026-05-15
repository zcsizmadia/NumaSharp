# NumaSharp

Core library providing NUMA topology detection, CPU affinity, NUMA-local memory pools, and a NUMA-aware task scheduler for .NET 10.

## Namespaces

### `NumaSharp.Core`

| Type | Description |
|------|-------------|
| `NumaTopology` | Singleton — detects NUMA nodes from `/sys` on Linux. |
| `NumaNode` | Describes one NUMA node: CPUs, memory, node ID. |
| `NumaMemoryPool` | `MemoryPool<byte>` backed by 64-byte-aligned native memory on a specific NUMA node. |
| `CpuAffinity` | Static helpers to pin threads to CPUs or NUMA nodes via `sched_setaffinity`. |

### `NumaSharp.Scheduling`

| Type | Description |
|------|-------------|
| `NumaTaskScheduler` | Multi-node `TaskScheduler` with `RoundRobin`, `LocalityFirst`, and `LeastLoaded` policies. |
| `NumaNodeScheduler` | Single-node `TaskScheduler` with all workers pinned to one NUMA node's CPUs. |
| `NumaTaskExtensions` | Extension methods: `RunOnNumaNode(int)`, `RunNumaLocal()`. |
| `NumaSchedulingPolicy` | Enum for scheduling policy selection. |

## Usage example

```csharp
using NumaSharp.Core;
using NumaSharp.Scheduling;

// Inspect NUMA topology
NumaTopology topology = NumaTopology.Instance;
Console.WriteLine($"{topology.NodeCount} nodes, {Environment.ProcessorCount} logical CPUs");

// NUMA-local memory pool
using NumaMemoryPool pool = new(numaNodeId: 0, blockSize: 4096);
using IMemoryOwner<byte> buf = pool.Rent();

// Schedule work on the calling thread's local NUMA node
await ((Action)(() => ProcessBuffer(buf.Memory))).RunNumaLocal();

// Schedule on a specific node
using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LeastLoaded);
int result = await scheduler.RunOnNode(1, () => ComputeOnNode1());
```

## Requirements

- .NET 10
- Linux x64 for full NUMA detection and CPU affinity
- No native bridge required
