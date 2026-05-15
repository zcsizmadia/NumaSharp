# NumaSharp.Transport API Reference

## Namespace: `NumaSharp.Transport`

Transport abstraction layer. Defines the contracts that all transport implementations must satisfy. This namespace lives inside the `NumaSharp.Transport.Epoll` assembly but is intentionally kept in the parent namespace so application code has a stable, implementation-agnostic API surface.

---

## `ITransportFactory`

Creates `IListener` instances bound to a specific endpoint.

```csharp
public interface ITransportFactory
```

### Methods

| Method | Description |
|--------|-------------|
| `BindAsync(EndPoint endpoint, CancellationToken ct = default)` | Binds to the given endpoint and returns an `IListener`. |

---

## `IListener`

Represents a bound server socket that accepts incoming connections.

```csharp
public interface IListener : IAsyncDisposable
```

### Properties

| Member | Type | Description |
|--------|------|-------------|
| `EndPoint` | `EndPoint` | The actual endpoint the listener is bound to (OS-assigned port if 0 was requested). |

### Methods

| Method | Description |
|--------|-------------|
| `AcceptAsync(CancellationToken ct = default)` | Waits for and returns the next incoming `IConnection`. Returns `null` when the listener is stopped. |
| `UnbindAsync(CancellationToken ct = default)` | Stops accepting new connections without disposing existing ones. |

---

## `IConnection`

Represents a single accepted TCP connection. Provides bidirectional pipe-based I/O.

```csharp
public interface IConnection : IAsyncDisposable
```

### Properties

| Member | Type | Description |
|--------|------|-------------|
| `ConnectionId` | `string` | Unique identifier for this connection. |
| `LocalEndPoint` | `EndPoint?` | Local (server) endpoint. |
| `RemoteEndPoint` | `EndPoint?` | Remote (client) endpoint. |
| `Transport` | `IDuplexPipe` | Pipeline for reading and writing bytes. |
| `Features` | `IConnectionFeatureCollection` | Extensible feature bag. |

### Notes

- All reads and writes are done through `Transport.Input` and `Transport.Output`.
- `DisposeAsync()` flushes pending output, sends a TCP FIN, and releases all resources.

---

## `ConnectionContext`

Base class for transport-provided `IConnection` implementations.

```csharp
public abstract class ConnectionContext : IConnection, IAsyncDisposable
```

Implementations provided by `NumaSharp.Transport.Epoll` extend this class.

---

## `IConnectionFeatureCollection`

An extensible dictionary of optional features attached to a connection.

```csharp
public interface IConnectionFeatureCollection
```

### Methods

| Method | Description |
|--------|-------------|
| `Get<TFeature>()` | Returns the feature of type `TFeature`, or `null` if not present. |
| `Set<TFeature>(TFeature? instance)` | Registers a feature instance. |
