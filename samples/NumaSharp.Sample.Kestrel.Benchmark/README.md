# NumaSharp — Kestrel Transport Benchmark

Measures HTTP throughput and latency of the default Kestrel `SocketTransport` against `NumaSharp.Transport.Epoll` on a 2-socket NUMA Linux server.

---

## Results

> **Environment:** .NET 10.0.8 · Ubuntu 22.04.5 LTS (kernel 6.8.0-94-generic) · 2 NUMA nodes · 96 CPUs · 512 keep-alive connections · 5 s measurement window

| Endpoint | Kestrel Default RPS | NumaSharp Epoll RPS | Throughput | Speedup |
|---|---:|---:|---:|:---:|
| GET /ping | 195,259 | 214,858 | — | **1.10×** |
| GET /data (4 KB) | 188,673 | 230,766 | 901 MB/s | **1.22×** |
| GET /data (64 KB) | 24,246 | 44,792 | 2,800 MB/s | **1.85× ★** |
| POST /echo (512 B) | 230,691 | 276,885 | 135 MB/s | **1.20×** |
| POST /echo (4 KB) | 124,880 | 155,252 | 607 MB/s | **1.24×** |
| POST /echo (64 KB) | 16,321 | 29,957 | 1,872 MB/s | **1.84× ★** |

**★ Largest gains on large payloads** — up to **1.85×** more RPS and **2,800 MB/s** throughput on 64 KB transfers, where NUMA-local buffer allocation and cache-warm I/O paths matter most.

### Latency comparison (P50 / P95 / P99)

| Endpoint | Kestrel Default | NumaSharp Epoll | P99 improvement |
|---|:---:|:---:|:---:|
| GET /ping | 2.5 / 6.8 / 10.5 ms | 1.8 / 5.6 / 9.5 ms | −10% |
| GET /data (4 KB) | 2.4 / 6.1 / 8.8 ms | 1.7 / 5.2 / 9.8 ms | −10% |
| GET /data (64 KB) | 17.2 / 47.4 / 70.0 ms | 10.6 / 20.5 / 25.7 ms | **−63%** |
| POST /echo (512 B) | 1.9 / 6.1 / 10.5 ms | 1.4 / 4.5 / 8.2 ms | −22% |
| POST /echo (4 KB) | 3.8 / 8.4 / 11.4 ms | 2.8 / 7.3 / 11.7 ms | −3% |
| POST /echo (64 KB) | 27.1 / 64.3 / 94.1 ms | 15.9 / 29.9 / 40.5 ms | **−57%** |

The full raw output is in [result-kestrel.txt](../../result-kestrel.txt).

---

## How it works

The benchmark spins up two ASP.NET Core hosts — one with the default `SocketTransport` and one with `NumaSharpTransport` — and runs each scenario sequentially with a 2 s warmup followed by 5 s of measurement.

Scenarios:

| Scenario | Payload |
|---|---|
| `GET /ping` | Empty response (latency baseline) |
| `GET /data` | 4 KB and 64 KB pre-built byte arrays |
| `POST /echo` | 512 B, 4 KB, and 64 KB echoed back |

512 connections are kept alive throughout to saturate the transport layer.

---

## Running

Requires a **Linux x64** machine with at least one NUMA node. For meaningful NUMA results, run on a multi-socket server.

```bash
dotnet run -c Release --project samples/NumaSharp.Sample.Kestrel.Benchmark
```

Results are printed to stdout in the same tabular format as shown above.
