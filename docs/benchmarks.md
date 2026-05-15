# NumaSharp Benchmarks

Benchmark results will be published here after running `samples/NumaSharp.Sample.Kestrel.Benchmark` on multi-NUMA hardware.

## Planned scenarios

| Scenario | Transport | Shards | Notes |
|----------|-----------|--------|-------|
| Baseline | ASP.NET Core `SocketTransport` | — | Default configuration |
| NumaSharp Epoll | `EpollTransportFactory` | 1 per NUMA node | NUMA-pinned shards |

## Metrics collected

- Requests per second (RPS) at saturation
- Latency percentiles: p50, p99, p999
- CPU utilization per NUMA node (`mpstat -P ALL`)
- Inter-node memory traffic (`numastat`)
- Kernel softirq balance across shards

## How to run

```bash
cd samples/NumaSharp.Sample.Kestrel.Benchmark
dotnet run -c Release
```

Results are printed to stdout and written to `results/` as JSON.

---

*Results will be added after the next benchmark run on production hardware.*
