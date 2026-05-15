using System.Net;

namespace NumaSharp.Transport;

/// <summary>
/// Creates transport listeners. Implement this interface to provide a custom
/// transport backend that plugs into NumaSharp.
/// </summary>
public interface ITransportFactory
{
    /// <summary>
    /// Binds to <paramref name="endPoint"/> and returns an <see cref="IListener"/>
    /// ready to accept connections.
    /// </summary>
    ValueTask<IListener> BindAsync(EndPoint endPoint, CancellationToken cancellationToken = default);
}
