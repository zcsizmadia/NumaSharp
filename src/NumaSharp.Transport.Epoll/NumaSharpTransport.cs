using System.IO.Pipelines;
using System.Net;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AspNetConnectionContext = Microsoft.AspNetCore.Connections.ConnectionContext;
namespace NumaSharp.Transport.Epoll;
/// <summary>
/// Adapts a NumaSharp <see cref="ITransportFactory"/> to the Kestrel
/// <see cref="IConnectionListenerFactory"/> contract.
/// </summary>
public sealed class NumaSharpConnectionListenerFactory : IConnectionListenerFactory
{
    private readonly ITransportFactory _transportFactory;
    private readonly ILoggerFactory _loggerFactory;
    /// <summary>Creates a <see cref="NumaSharpConnectionListenerFactory"/>.</summary>
    public NumaSharpConnectionListenerFactory(
        ITransportFactory transportFactory,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(transportFactory);
        _transportFactory = transportFactory;
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }
    /// <inheritdoc />
    public async ValueTask<IConnectionListener> BindAsync(
        EndPoint endpoint, CancellationToken cancellationToken = default)
    {
        IListener listener = await _transportFactory
            .BindAsync(endpoint, cancellationToken)
            .ConfigureAwait(false);
        return new NumaSharpConnectionListener(listener);
    }
}
/// <summary>Wraps a NumaSharp <see cref="IListener"/> as a Kestrel <see cref="IConnectionListener"/>.</summary>
internal sealed class NumaSharpConnectionListener(IListener listener) : IConnectionListener
{
    /// <inheritdoc />
    public EndPoint EndPoint => listener.EndPoint;
    /// <inheritdoc />
    public async ValueTask<AspNetConnectionContext?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        IConnection? conn = await listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
        return conn is null ? null : new NumaSharpConnectionContext(conn);
    }
    /// <inheritdoc />
    public ValueTask UnbindAsync(CancellationToken cancellationToken = default) =>
        listener.UnbindAsync(cancellationToken);
    /// <inheritdoc />
    public ValueTask DisposeAsync() => listener.DisposeAsync();
}
/// <summary>Wraps a NumaSharp <see cref="IConnection"/> as an ASP.NET Core <see cref="AspNetConnectionContext"/>.</summary>
internal sealed class NumaSharpConnectionContext(IConnection connection) : AspNetConnectionContext
{
    /// <inheritdoc />
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    /// <inheritdoc />
    public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
    /// <inheritdoc />
    public override string ConnectionId
    {
        get => connection.ConnectionId;
        set { /* immutable */ }
    }
    /// <inheritdoc />
    public override IDuplexPipe Transport
    {
        get => connection.Transport;
        set { /* immutable */ }
    }
    /// <inheritdoc />
    public override EndPoint? LocalEndPoint
    {
        get => connection.LocalEndPoint;
        set { /* immutable */ }
    }
    /// <inheritdoc />
    public override EndPoint? RemoteEndPoint
    {
        get => connection.RemoteEndPoint;
        set { /* immutable */ }
    }
    /// <inheritdoc />
    public override CancellationToken ConnectionClosed
    {
        get => connection.ConnectionClosed;
        set { /* immutable */ }
    }
    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
