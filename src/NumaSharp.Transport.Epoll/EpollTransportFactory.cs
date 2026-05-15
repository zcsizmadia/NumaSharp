using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NumaSharp.Core;
using NumaSharp.Scheduling;
namespace NumaSharp.Transport.Epoll;
/// <summary>
/// Creates NUMA-aware epoll-backed transport listeners.
/// Linux-only; throws <see cref="PlatformNotSupportedException"/> on other platforms.
/// </summary>
public sealed class EpollTransportFactory : ITransportFactory
{
    private readonly EpollTransportOptions _options;
    private readonly NumaTaskScheduler     _scheduler;
    private readonly ILoggerFactory        _loggerFactory;
    /// <summary>Creates a new <see cref="EpollTransportFactory"/>.</summary>
    /// <param name="scheduler">
    /// The NUMA-aware task scheduler that owns the per-node thread pools.
    /// Connections are routed to the NUMA node matching the NIC interrupt CPU via SO_INCOMING_CPU.
    /// </param>
    /// <param name="options">Optional transport configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public EpollTransportFactory(
        NumaTaskScheduler      scheduler,
        EpollTransportOptions? options       = null,
        ILoggerFactory?        loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler     = scheduler;
        _options       = options ?? new EpollTransportOptions();
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }
    /// <inheritdoc />
    public async ValueTask<IListener> BindAsync(EndPoint endPoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException("EpollTransportFactory requires Linux.");
        }
        NumaTopology topology = _scheduler.Topology;
        ILogger<EpollListener> logger = _loggerFactory.CreateLogger<EpollListener>();
        int shards = _options.ListenerShards > 0 ? _options.ListenerShards : _scheduler.NodeCount;
        if (shards <= 1)
        {
            NumaNode node = topology.Nodes[0];
            EpollListener listener = new(endPoint, _options, node, topology, logger);
            await listener.BindAsync(cancellationToken).ConfigureAwait(false);
            return listener;
        }
        // Multi-shard: one epoll fd + poll thread per shard, all sharing the same port via
        // SO_REUSEPORT. The kernel distributes accepted connections across the shards.
        EpollListener[] shardListeners = new EpollListener[shards];
        for (int i = 0; i < shards; i++)
        {
            NumaNode node = topology.Nodes[i % topology.NodeCount];
            shardListeners[i] = new EpollListener(endPoint, _options, node, topology, logger);
            await shardListeners[i].BindAsync(cancellationToken).ConfigureAwait(false);
        }
        return new MultiShardEpollListener(shardListeners, logger);
    }
}
