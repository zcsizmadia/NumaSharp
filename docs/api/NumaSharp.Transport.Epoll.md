# NumaSharp.Transport.Epoll API Reference

## Namespace: `NumaSharp.Transport.Epoll`

Linux epoll-based TCP transport. NUMA-aware: each listener shard is owned by a dedicated `NumaNodeScheduler`, so accept loops and I/O completions stay local to the NUMA node that owns the connection.

No native bridge is required — all epoll interaction goes through .NET's built-in `Socket` with `SocketAsyncEngine`.

---

## `EpollTransportFactory`

Creates and binds epoll listeners. Implements `ITransportFactory`.

```csharp
public sealed class EpollTransportFactory : ITransportFactory
```

### Constructor

```csharp
public EpollTransportFactory(
    NumaTaskScheduler scheduler,
    EpollTransportOptions? options = null,
    ILoggerFactory? loggerFactory = null)
```

| Parameter | Description |
|-----------|-------------|
| `scheduler` | Controls which NUMA nodes the listener shards run on. |
| `options` | Optional tuning parameters. Defaults to `new EpollTransportOptions()`. |
| `loggerFactory` | Optional. Uses `NullLoggerFactory.Instance` when not supplied. |

**Throws** `PlatformNotSupportedException` on non-Linux platforms (called at bind time).

### Methods

| Method | Description |
|--------|-------------|
| `BindAsync(EndPoint endpoint, CancellationToken ct = default)` | Binds to the endpoint, creates one `EpollListener` shard per configured NUMA node, and wraps them in a `MultiShardEpollListener`. |

---

## `EpollTransportOptions`

Tuning parameters for the epoll transport.

```csharp
public sealed class EpollTransportOptions
```

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ListenBacklog` | `int` | `512` | `SO_BACKLOG` passed to `listen(2)`. |
| `MaxConnections` | `int` | `int.MaxValue` | Soft cap on concurrent connections per shard. |
| `ListenerShards` | `int` | `0` | Number of listener shards. `0` = one shard per NUMA node in the scheduler's topology. |

---

## `EpollListener`

A single listener shard bound to one NUMA node. Internal — do not instantiate directly.

```csharp
internal sealed class EpollListener : IListener
```

Accepts connections on a pinned thread, then hands each connection off to a `NumaNodeScheduler` worker for I/O processing.

---

## `MultiShardEpollListener`

Aggregates multiple `EpollListener` shards behind a single `IListener` interface using `SO_REUSEPORT`. The OS kernel distributes incoming connections across shards, avoiding a single accept-loop bottleneck.

```csharp
internal sealed class MultiShardEpollListener : IListener
```

---

## `EpollConnection`

Represents one accepted TCP connection. Internal — obtained via `IListener.AcceptAsync`.

```csharp
internal sealed class EpollConnection : ConnectionContext
```

| Property | Description |
|----------|-------------|
| `ConnectionId` | Unique GUID-based identifier. |
| `LocalEndPoint` | Server-side endpoint. |
| `RemoteEndPoint` | Client endpoint. |
| `Transport` | `IDuplexPipe` backed by `Pipe` — use `Input` to read, `Output` to write. |

---

## Kestrel integration: `NumaSharpKestrelExtensions`

Extension method to plug the epoll transport into ASP.NET Core / Kestrel.

```csharp
public static class NumaSharpKestrelExtensions
```

### Methods

| Method | Description |
|--------|-------------|
| `UseNumaSharpTransport(this IWebHostBuilder, ITransportFactory)` | Replaces Kestrel's default `SocketTransport` with the supplied `ITransportFactory`. |

### Example

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

using NumaTaskScheduler scheduler = new(NumaSchedulingPolicy.LocalityFirst);
EpollTransportFactory transport = new(scheduler);

builder.WebHost.UseNumaSharpTransport(transport);

WebApplication app = builder.Build();
app.MapGet("/ping", () => "pong");
await app.RunAsync();
```

---

## Architecture: NUMA-aware connection flow

```
                     Kernel (SO_REUSEPORT)
                      /       |       \
             Shard 0        Shard 1        Shard N
         (Node 0 CPUs)  (Node 1 CPUs)  (Node N CPUs)
              |               |               |
         AcceptLoop       AcceptLoop       AcceptLoop
              |               |               |
         EpollConnection  EpollConnection  EpollConnection
         (pinned to         (pinned to       (pinned to
          node 0)            node 1)          node N)
```

Each connection's entire lifetime — accept, read, write, close — stays on the NUMA node where it was accepted. No cross-NUMA cache line bouncing.
