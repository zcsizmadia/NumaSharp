# NumaSharp Performance Guide

## Why NUMA awareness matters

Modern servers have 2–8 NUMA nodes. Each node has its own CPU cores and local DDR channels. When a CPU accesses memory attached to a **remote** node it pays a latency penalty of 30–80 ns extra (depending on the interconnect), and saturates the inter-node fabric instead of the local memory bus.

For high-throughput servers this means:
- A buffer allocated on node 0 but read by a thread on node 1 → cache misses + remote DRAM latency.
- A connection accepted on node 0 but handed to a thread pool worker on node 1 → every packet read traverses the interconnect.
- A `MemoryPool` shared across all threads → NUMA-remote allocations from nodes that didn't initialize the slab.

NumaSharp eliminates all three patterns.

---

## What NumaSharp does differently

### 1. NUMA-local memory allocation

`NumaMemoryPool` allocates native buffers using `NativeMemory.AlignedAlloc` (64-byte alignment) on the initiating thread. When that thread is pinned to a NUMA node's CPUs via `CpuAffinity`, the OS first-touch policy places the physical page on the local DRAM bank.

Each node in a multi-node system gets its own `NumaMemoryPool` instance. Buffer rental (`Rent()`) is a thread-local stack pop — O(1) with no locking.

### 2. Pinned scheduler threads

`NumaNodeScheduler` starts its worker threads inside `NumaTopology`-aware constructors that immediately call `sched_setaffinity` to bind each thread to its node's CPU set. Tasks queued to a node scheduler never migrate to another node.

### 3. Connection sharding with `SO_REUSEPORT`

`EpollTransportFactory` creates one listener shard per NUMA node. The Linux kernel distributes incoming connections across shards at the TCP/IP stack level — no userspace lock, no funnel. Each shard's accept loop runs on a pinned worker and creates connections whose pipelines are backed by that node's `NumaMemoryPool`.

---

## Benchmark methodology

Results will be published after running `NumaSharp.Sample.Kestrel.Benchmark` on a multi-node server. See [docs/benchmarks.md](benchmarks.md).

Planned scenarios:
- **Baseline**: ASP.NET Core with default `SocketTransport`
- **NumaSharp Epoll**: `EpollTransportFactory` with 1 shard per NUMA node
- Metrics: RPS, p50/p99/p999 latency, CPU utilization per node, inter-node DRAM traffic (via `perf`)

---

## Tuning guide

### Thread count per node

Default: `node.ProcessorCount` threads per `NumaNodeScheduler`. For I/O-heavy workloads (waiting on pipelines), more threads than CPUs can improve throughput. For CPU-bound workloads, match exactly.

```csharp
new NumaNodeScheduler(node, threadCount: node.ProcessorCount * 2); // I/O bound
new NumaNodeScheduler(node, threadCount: node.ProcessorCount);     // CPU bound
```

### Listener shards

Default: one shard per NUMA node. Increase for extremely high connection rates on a single node (rare):

```csharp
new EpollTransportOptions { ListenerShards = 4 }
```

### Scheduling policy

| Policy | Best for |
|--------|----------|
| `LocalityFirst` | Mixed workloads where the calling thread's node is the right choice most of the time. |
| `LeastLoaded` | Bursty workloads where one node can become temporarily saturated. |
| `RoundRobin` | Uniform workloads on symmetric hardware. |

### Memory pool block size

Choose a block size that matches your typical request/response payload:

| Workload | Recommended `blockSize` |
|----------|------------------------|
| Tiny APIs (< 1 KB) | 4096 (default) |
| JSON APIs (1–8 KB) | 8192 |
| Large file transfer | 65536 |

---

## Common pitfalls

| Pitfall | Fix |
|---------|-----|
| Creating `NumaMemoryPool` on a non-pinned thread | Pin the creating thread first with `CpuAffinity.SetCurrentThreadAffinityForCpus` |
| Sharing a `NumaMemoryPool` across nodes | Create one pool per node and use the matching pool in each node's scheduler workers |
| Using `Task.Run` to dispatch work from a pinned thread | Use `scheduler.RunOnNode(sameNode, ...)` to stay on the same node |
| Not disposing `NumaMemoryPool` | Use `using` — native memory is not collected by the GC |
