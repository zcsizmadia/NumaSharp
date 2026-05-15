# NumaSharp.Sample.Benchmark

Micro-benchmarks for NumaSharp's scheduling and memory allocation primitives.

## Benchmarks

| Benchmark | What it measures |
|-----------|-----------------|
| `SchedulerThroughputBenchmark` | Tasks/sec through `NumaTaskScheduler` with each policy |
| `MemoryPoolBenchmark` | Rent + return throughput vs `ArrayPool<byte>` |
| `CrossNodeBenchmark` | Cost of cross-node task handoff vs same-node |
| `ScaleOutBenchmark` | Throughput scaling from 1 to N NUMA nodes |

## How to run

```bash
cd samples/NumaSharp.Sample.Benchmark
dotnet run -c Release
```

Output includes per-benchmark RPS, latency percentiles, and a comparison table.

## Notes

- Benchmarks automatically adapt to the detected NUMA topology.
- On a single-node machine (or VMs), cross-node benchmarks measure thread-switch overhead only.
- Run on a real multi-socket NUMA server for meaningful cross-node numbers.
