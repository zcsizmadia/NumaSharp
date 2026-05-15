# NumaSharp Architecture

## Overview

NumaSharp is a **NUMA-aware, high-performance .NET runtime layer** for server applications. It exposes NUMA hardware to .NET developers through three focused layers:

```
┌────────────────────────────────────────────────────────────┐
│  ASP.NET Core / Kestrel  (your application)                │
├────────────────────────────────────────────────────────────┤
│  NumaSharp.Transport.Epoll  (ITransportFactory, Kestrel     │
│  adapter, epoll listener shards, EpollConnection)          │
├────────────────────────────────────────────────────────────┤
│  NumaSharp.Scheduling  (NumaTaskScheduler,                  │
│  NumaNodeScheduler, policies)                              │
├────────────────────────────────────────────────────────────┤
│  NumaSharp.Core  (NumaTopology, NumaNode, NumaMemoryPool,   │
│  CpuAffinity)                                              │
└────────────────────────────────────────────────────────────┘
```

---

## Layer 1 — Core primitives

`NumaSharp.Core` is a dependency-free layer that talks directly to the OS.

| Type | Responsibility |
|------|----------------|
| `NumaTopology` | Reads `/sys/devices/system/node/` on Linux to discover nodes, CPUs, and memory. Falls back to a synthetic single-node on other platforms. |
| `NumaNode` | Value-like descriptor: `NodeId`, `CpuIds`, `MemoryBytes`. Immutable after construction. |
| `NumaMemoryPool` | `MemoryPool<byte>` backed by `NativeMemory.AlignedAlloc` (64-byte aligned). Per-NUMA-node allocator; returned blocks are reused via a thread-local stack. |
| `CpuAffinity` | Wraps `sched_setaffinity(2)` to pin threads to specific CPUs or NUMA nodes. |

### Design goals

- **Zero allocation on hot paths** — `Rent()` returns a pooled block from a pre-allocated slab.
- **No cross-NUMA allocations** — Each `NumaMemoryPool` instance is associated with one node and uses OS-level NUMA placement hints where available.
- **Portable** — All types work on Windows/macOS (with degraded NUMA detection); no native bridge is required.

---

## Layer 2 — Scheduling

`NumaSharp.Scheduling` builds on Core to route .NET `Task`s to NUMA-local threads.

### `NumaTaskScheduler`

- Creates one `NumaNodeScheduler` per NUMA node.
- Exposes three scheduling policies: `RoundRobin`, `LocalityFirst`, `LeastLoaded`.
- `Shared` singleton uses `LocalityFirst`, which inspects the calling thread's CPU affinity to pick the nearest node.

### `NumaNodeScheduler`

- Owns a fixed thread pool (default: one thread per CPU in the node).
- All threads call `CpuAffinity.SetCurrentThreadAffinityForCpus(node.CpuIds)` at startup.
- Work never migrates to a different node's threads.

### Cross-node communication

- Use `scheduler.RunOnNode(n, ...)` to move work between nodes explicitly.
- There is no shared lock; each node group has its own `BlockingCollection<Task>`.

---

## Layer 3 — Transport

`NumaSharp.Transport.Epoll` wires the scheduling layer into the ASP.NET Core connection model.

### Listener sharding

When `EpollTransportFactory.BindAsync()` is called:

1. It creates `ListenerShards` (default: one per NUMA node) `EpollListener` instances.
2. Each shard binds with `SO_REUSEPORT` so the kernel distributes connections across shards.
3. Each shard's accept loop runs on a `NumaNodeScheduler` pinned to its NUMA node.

### Connection lifecycle

```
Client SYN → Kernel → Shard N (node N) → EpollListener.AcceptAsync()
                                                   │
                                          EpollConnection created
                                                   │
                                    Kestrel handler runs on node N worker
                                                   │
                                     All I/O: pipe reads/writes on node N
                                                   │
                                    Client closes → DisposeAsync() on node N
```

The connection never leaves its NUMA node. Request parsing, application logic, and response serialization all touch the same NUMA-local CPU caches.

### Pipeline I/O

All reads and writes go through `System.IO.Pipelines`. No `byte[]` copies. The read buffer is allocated from the `NumaMemoryPool` of the owning node.

---

## Project structure

```
src/
  NumaSharp/                         Core + Scheduling
    Core/
      NumaTopology.cs
      NumaNode.cs
      NumaMemoryPool.cs
      CpuAffinity.cs
    Scheduling/
      NumaTaskScheduler.cs
      NumaNodeScheduler.cs
      NumaTaskExtensions.cs

  NumaSharp.Transport.Epoll/         Transport abstraction + epoll implementation
    EpollTransportFactory.cs
    EpollListener.cs
    MultiShardEpollListener.cs
    EpollConnection.cs
    EpollTransportOptions.cs
    NumaSharpKestrelExtensions.cs
    Interop/EpollInterop.cs

tests/
  NumaSharp.Core.Tests/             84 tests
  NumaSharp.Scheduler.Tests/        56 tests
  NumaSharp.IntegrationTests/       14 tests

samples/
  NumaSharp.Sample.Benchmark/       NUMA scheduling micro-benchmarks
  NumaSharp.Sample.Kestrel.Benchmark/ Kestrel throughput/latency benchmarks
```

---

## Threading model

| Thread type | Pinned to | Runs |
|-------------|-----------|------|
| `NumaNodeScheduler` worker | One NUMA node's CPU set | All `Task`s queued to that node |
| Kestrel thread pool thread | Not pinned (unless routed) | ASP.NET Core middleware |

> **Key invariant**: NumaSharp never uses shared locks between nodes. Each node group is completely independent.

---

## Supported platforms

| Platform | NUMA detection | CPU affinity | Epoll transport |
|----------|---------------|--------------|-----------------|
| Linux x64 | ✅ Full (`/sys`) | ✅ `sched_setaffinity` | ✅ |
| Windows x64 | ⚠️ Partial (WinAPI) | ⚠️ Partial | ❌ not supported |
| macOS arm64/x64 | ❌ Single node fallback | ❌ No-op | ❌ not supported |
