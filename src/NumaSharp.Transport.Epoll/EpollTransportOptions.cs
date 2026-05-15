namespace NumaSharp.Transport.Epoll;

/// <summary>Configuration options for the epoll transport.</summary>
public sealed class EpollTransportOptions
{
    /// <summary>The backlog size passed to <c>listen(2)</c>. Default: 512.</summary>
    public int ListenBacklog { get; set; } = 512;

    /// <summary>Maximum number of concurrent connections per listener. Default: <c>int.MaxValue</c>.</summary>
    public int MaxConnections { get; set; } = int.MaxValue;

    /// <summary>
    /// Number of parallel listener shards created per <c>BindAsync</c> call.
    /// Each shard runs its own epoll fd and poll thread, and all shards bind to the same port
    /// via <c>SO_REUSEPORT</c> — the kernel load-balances accepted connections across them.
    /// <para>Default: 0 — one shard per NUMA node in the scheduler's topology.</para>
    /// </summary>
    public int ListenerShards { get; set; }
}
