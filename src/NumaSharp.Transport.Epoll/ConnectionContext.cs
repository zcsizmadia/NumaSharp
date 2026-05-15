using System.IO.Pipelines;
using System.Net;

namespace NumaSharp.Transport;

/// <summary>
/// Base class for <see cref="IConnection"/> implementations.
/// Provides default feature-collection handling and a cancellable close signal.
/// </summary>
public abstract class ConnectionContext : IConnection
{
    private readonly ConnectionFeatures _features = new();
    private readonly CancellationTokenSource _closedCts = new();

    /// <inheritdoc />
    public abstract string ConnectionId { get; }

    /// <inheritdoc />
    public abstract EndPoint LocalEndPoint { get; }

    /// <inheritdoc />
    public abstract EndPoint RemoteEndPoint { get; }

    /// <inheritdoc />
    public abstract IDuplexPipe Transport { get; }

    /// <inheritdoc />
    public CancellationToken ConnectionClosed => _closedCts.Token;

    /// <inheritdoc />
    public IConnectionFeatures Features => _features;

    /// <inheritdoc />
    public virtual ValueTask AbortAsync(Exception? reason = null) => DisposeAsync();

    /// <summary>Signals <see cref="ConnectionClosed"/> to all observers.</summary>
    protected void SignalClosed() => _closedCts.Cancel();

    /// <inheritdoc />
    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        SignalClosed();
        await Transport.Output.CompleteAsync().ConfigureAwait(false);
        _closedCts.Dispose();
    }
}
