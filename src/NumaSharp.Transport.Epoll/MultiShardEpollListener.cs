using System.Net;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
namespace NumaSharp.Transport.Epoll;
/// <summary>
/// An <see cref="IListener"/> that aggregates multiple <see cref="EpollListener"/> shards
/// bound to the same port via <c>SO_REUSEPORT</c>.
/// <para>
/// Each shard runs its own epoll fd and dedicated poll thread pinned to a NUMA node.
/// The kernel distributes accepted connections across shards, allowing the accept pipeline
/// to scale across all available CPU cores rather than being bottlenecked on a single fd.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class MultiShardEpollListener : IListener
{
    private readonly EpollListener[] _shards;
    private readonly Channel<IConnection> _merged;
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private readonly Task _fanInTask;
    internal MultiShardEpollListener(EpollListener[] shards, ILogger logger)
    {
        _shards = shards;
        _logger = logger;
        // Unbounded so that fast shards never block while slow ones drain.
        // The Kestrel accept loop applies the true flow-control pressure.
        _merged = Channel.CreateUnbounded<IConnection>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        _fanInTask = FanInAsync();
    }
    /// <inheritdoc />
    public EndPoint EndPoint => _shards[0].EndPoint;
    /// <inheritdoc />
    public async ValueTask<IConnection?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _merged.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }
    /// <inheritdoc />
    public async ValueTask UnbindAsync(CancellationToken cancellationToken = default)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        Task[] tasks = new Task[_shards.Length];
        for (int i = 0; i < _shards.Length; i++)
        {
            tasks[i] = _shards[i].UnbindAsync(cancellationToken).AsTask();
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        await _fanInTask.ConfigureAwait(false);
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await UnbindAsync().ConfigureAwait(false);
        foreach (EpollListener shard in _shards)
        {
            await shard.DisposeAsync().ConfigureAwait(false);
        }
        _cts.Dispose();
    }
    // One forwarding task per shard pumps accepted connections into the merged channel.
    private Task FanInAsync()
    {
        Task[] pumps = new Task[_shards.Length];
        for (int i = 0; i < _shards.Length; i++)
        {
            pumps[i] = PumpShardAsync(_shards[i]);
        }
        return Task.WhenAll(pumps).ContinueWith(
            t => { _merged.Writer.TryComplete(t.Exception); },
            TaskScheduler.Default);
    }
    private async Task PumpShardAsync(EpollListener shard)
    {
        try
        {
            while (true)
            {
                IConnection? conn = await shard.AcceptAsync(_cts.Token).ConfigureAwait(false);
                if (conn is null)
                {
                    break;
                }
                await _merged.Writer.WriteAsync(conn, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogShardPumpError(_logger, ex);
        }
    }
    [LoggerMessage(Level = LogLevel.Error, Message = "Epoll shard pump terminated unexpectedly.")]
    private static partial void LogShardPumpError(ILogger logger, Exception ex);
}
