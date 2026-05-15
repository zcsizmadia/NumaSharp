using System.Net;

namespace NumaSharp.Transport;

/// <summary>
/// Accepts incoming connections on a bound endpoint.
/// </summary>
public interface IListener : IAsyncDisposable
{
    /// <summary>Gets the endpoint this listener is bound to (may differ from the requested endpoint, e.g. for port 0).</summary>
    EndPoint EndPoint { get; }

    /// <summary>
    /// Asynchronously waits for and returns the next incoming <see cref="IConnection"/>.
    /// Returns <c>null</c> when the listener is unbound or disposed.
    /// </summary>
    ValueTask<IConnection?> AcceptAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops accepting new connections without closing existing ones.</summary>
    ValueTask UnbindAsync(CancellationToken cancellationToken = default);
}
