using System.IO.Pipelines;
using System.Net;

namespace NumaSharp.Transport;

/// <summary>
/// Represents an active, bidirectional network connection.
/// Provides zero-copy I/O via <see cref="System.IO.Pipelines"/>.
/// </summary>
public interface IConnection : IAsyncDisposable
{
    /// <summary>Gets a unique identifier for this connection.</summary>
    string ConnectionId { get; }

    /// <summary>Gets the local endpoint of this connection.</summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>Gets the remote endpoint of this connection.</summary>
    EndPoint RemoteEndPoint { get; }

    /// <summary>
    /// Gets the duplex pipe for zero-copy reads and writes.
    /// Use <c>Transport.Input</c> to read incoming data and
    /// <c>Transport.Output</c> to write outgoing data.
    /// </summary>
    IDuplexPipe Transport { get; }

    /// <summary>Gets a <see cref="CancellationToken"/> that fires when the connection is closed.</summary>
    CancellationToken ConnectionClosed { get; }

    /// <summary>
    /// Gets or sets application-defined features attached to this connection.
    /// Use <see cref="IConnectionFeatures"/> for structured access.
    /// </summary>
    IConnectionFeatures Features { get; }

    /// <summary>Immediately aborts the connection, optionally providing a reason.</summary>
    ValueTask AbortAsync(Exception? reason = null);
}
