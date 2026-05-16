# NumaSharp

**NUMA-aware, high-performance .NET runtime layer for Linux servers.**

NumaSharp gives .NET server applications the tools they need to exploit multi-socket NUMA hardware: topology detection, CPU-pinned scheduling, NUMA-local memory pools, and an epoll-based Kestrel transport — all with zero native bridges and clean .NET 10 APIs.

---

## Why NUMA awareness matters

Modern servers have 2–8 NUMA nodes. Each node has its own CPU cores and local DDR memory channels. When threads access memory on a **remote** node the penalty is 30–80 ns of extra latency *per access* — and cross-socket traffic saturates the interconnect instead of the local memory bus.

NumaSharp eliminates cross-NUMA traffic on hot paths by:

1. **Pinning scheduler threads** to specific NUMA nodes — tasks never migrate.
2. **Allocating I/O buffers from NUMA-local native memory** — no cross-socket DRAM reads.
3. **Sharding the accept loop** across nodes with `SO_REUSEPORT` — each connection lives entirely on one node.

---

## Packages

| Package | Description |
|---------|-------------|
| `NumaSharp` | Core primitives (topology, CPU affinity, memory pool) + NUMA-aware task scheduler |
| `NumaSharp.Transport.Epoll` | epoll-based Kestrel transport with NUMA-sharded accept loops |

---

## Quick start

### NUMA-aware task scheduling

```csharp
using NumaSharp.Scheduling;

// One worker group per NUMA node, all threads pinned to their node's CPUs
using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LocalityFirst);

// Schedule work on a specific node
await scheduler.RunOnNode(nodeIndex: 0, () =>
{
    // Runs on a thread pinned to node 0 — no cross-NUMA memory traffic
    ProcessNodeLocalData();
});

// Or use the shared singleton
await ((Action)UpdateCache).RunNumaLocal(); // routes to caller's NUMA node
```

### NUMA-local memory pools

```csharp
using NumaSharp.Core;

// One pool per NUMA node — allocations stay on local DRAM
using NumaMemoryPool pool0 = new(numaNodeId: 0, blockSize: 4096);
using NumaMemoryPool pool1 = new(numaNodeId: 1, blockSize: 4096);

using IMemoryOwner<byte> buf = pool0.Rent(); // 64-byte aligned native memory
buf.Memory.Span.Fill(0);                     // zero-copy I/O ready
```

### Kestrel with NUMA-aware epoll transport

```csharp
using NumaSharp.Scheduling;
using NumaSharp.Transport.Epoll;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LocalityFirst);
EpollTransportFactory transport = new(scheduler);

builder.WebHost.UseNumaSharpTransport(transport);

WebApplication app = builder.Build();
app.MapGet("/ping", () => "pong");
await app.RunAsync();
```

At startup, one epoll listener shard is created per NUMA node. The kernel distributes connections across shards via `SO_REUSEPORT`. Every connection — from accept through request parsing to response flush — stays on the node where it arrived.

---

## Topology inspection

```csharp
using NumaSharp.Core;

NumaTopology topology = NumaTopology.Instance;
Console.WriteLine($"{topology.NodeCount} NUMA nodes");

foreach (NumaNode node in topology.Nodes)
{
    Console.WriteLine($"  Node {node.NodeId}: CPUs {string.Join(',', node.CpuIds)}, "
                    + $"{node.MemoryBytes / (1L << 30)} GB");
}
```

---

## Benchmarks

See [docs/benchmarks.md](docs/benchmarks.md) for the benchmark methodology and results (populated after the next Kestrel benchmark run on multi-NUMA hardware).

---

## Documentation

| Document | Description |
|----------|-------------|
| [docs/architecture.md](docs/architecture.md) | How the three layers fit together |
| [docs/performance.md](docs/performance.md) | NUMA performance guide and tuning tips |
| [docs/benchmarks.md](docs/benchmarks.md) | Kestrel benchmark results |
| [docs/api/NumaSharp.Core.md](docs/api/NumaSharp.Core.md) | Core API reference |
| [docs/api/NumaSharp.Scheduling.md](docs/api/NumaSharp.Scheduling.md) | Scheduling API reference |
| [docs/api/NumaSharp.Transport.md](docs/api/NumaSharp.Transport.md) | Transport abstraction reference |
| [docs/api/NumaSharp.Transport.Epoll.md](docs/api/NumaSharp.Transport.Epoll.md) | Epoll transport reference |

---

## Requirements

- **.NET 10**
- **Linux x64** for epoll transport and full NUMA detection
- CPU affinity (`sched_setaffinity`) available (standard on all Linux kernels)
- No native bridge or external C libraries required

---

## Project structure

```
src/
  NumaSharp/                        Core + Scheduling
  NumaSharp.Transport.Epoll/        Epoll transport + Kestrel adapter
tests/
  NumaSharp.Core.Tests/             84 tests
  NumaSharp.Scheduler.Tests/        56 tests
  NumaSharp.IntegrationTests/       14 tests
samples/
  NumaSharp.Sample.Benchmark/       NUMA scheduling micro-benchmarks
  NumaSharp.Sample.Kestrel.Benchmark/ Kestrel throughput benchmarks
docs/
  architecture.md
  performance.md
  benchmarks.md
  api/
```

---

## Benchmarks

> **Environment:** .NET 10.0.8 · Ubuntu 22.04.5 LTS (kernel 6.8.0-94-generic) · 2 NUMA nodes · 96 CPUs · 512 keep-alive connections · 5 s measurement window

Comparison between the default Kestrel `SocketTransport` and `NumaSharp.Transport.Epoll` running on a 2-socket NUMA server.

| Endpoint | Kestrel Default RPS | NumaSharp Epoll RPS | Throughput | Speedup |
|---|---:|---:|---:|:---:|
| GET /ping | 195,259 | 214,858 | — | **1.10×** |
| GET /data (4 KB) | 188,673 | 230,766 | 901 MB/s | **1.22×** |
| GET /data (64 KB) | 24,246 | 44,792 | 2,800 MB/s | **1.85× ★** |
| POST /echo (512 B) | 230,691 | 276,885 | 135 MB/s | **1.20×** |
| POST /echo (4 KB) | 124,880 | 155,252 | 607 MB/s | **1.24×** |
| POST /echo (64 KB) | 16,321 | 29,957 | 1,872 MB/s | **1.84× ★** |

**★ Largest gains on large payloads** — up to **1.85×** more RPS on 64 KB transfers, where NUMA-local buffer allocation and cache-warm I/O paths make the biggest difference.

Latency improvements (P99, GET /data 64 KB): **70.0 ms → 25.7 ms** (63% reduction).

---

## License

MIT — see [LICENSE](LICENSE).
