using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NumaSharp.Core;
using NumaSharp.Transport.Epoll.Interop;
namespace NumaSharp.Transport.Epoll;
/// <summary>
/// Pure-managed epoll-based TCP listener for Linux.
/// Each listener instance runs one dedicated OS thread pinned to a NUMA node.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class EpollListener : IListener
{
    // Linux SOL_SOCKET / SO_INCOMING_CPU (kernel 3.19+).
    private const int SolSocket = 1;
    private const int SoIncomingCpu = 49;
    private readonly EndPoint _requestedEndPoint;
    private readonly EpollTransportOptions _options;
    private readonly NumaNode _node;
    private readonly NumaTopology _topology;
    private readonly ILogger<EpollListener> _logger;
    private readonly Channel<IConnection> _acceptQueue;
    private readonly CancellationTokenSource _cts = new();
    private Socket? _listenSocket;
    private int _epollFd = -1;
    private int _wakeupFd = -1;           // eventfd(2) added to epoll for clean shutdown
    private MemoryPool<byte>? _pool;      // NUMA-local pool shared by all connections on this listener
    private EndPoint _boundEndPoint = null!;
    private Thread? _epollThread;
    /// <inheritdoc />
    public EndPoint EndPoint => _boundEndPoint;
    internal EpollListener(
        EndPoint endPoint,
        EpollTransportOptions options,
        NumaNode node,
        NumaTopology topology,
        ILogger<EpollListener> logger)
    {
        _requestedEndPoint = endPoint;
        _options = options;
        _node = node;
        _topology = topology;
        _logger = logger;
        _acceptQueue = Channel.CreateBounded<IConnection>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
    }
    internal ValueTask BindAsync(CancellationToken cancellationToken)
    {
        AddressFamily family = _requestedEndPoint is IPEndPoint ip
            ? ip.AddressFamily
            : AddressFamily.InterNetwork;
        _listenSocket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
        _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        // SO_REUSEPORT: multiple sockets can bind the same address/port; the kernel distributes
        // connections across them when ListenerShards > 1.
        Span<byte> reusePort = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(reusePort, 1);
        _listenSocket.SetRawSocketOption(SolSocket, 15 /* SO_REUSEPORT */, reusePort);
        _listenSocket.Blocking = false;
        _listenSocket.NoDelay = true;
        _listenSocket.Bind(_requestedEndPoint);
        _listenSocket.Listen(_options.ListenBacklog);
        _boundEndPoint = _listenSocket.LocalEndPoint!;
        _epollFd = EpollInterop.EpollCreate1(EpollInterop.EpollCloexec);
        if (_epollFd < 0)
        {
            throw new InvalidOperationException(
                $"epoll_create1 failed: errno {Marshal.GetLastPInvokeError()}.");
        }
        // Listen socket — edge-triggered, store fd so the poll loop can distinguish events.
        int listenFd = _listenSocket.SafeHandle.DangerousGetHandle().ToInt32();
        EpollEvent ev = new()
        {
            Events = EpollInterop.EpollIn | EpollInterop.EpollEdgeTriggered,
            Data = new EpollData { Fd = listenFd }
        };
        EpollInterop.EpollCtl(_epollFd, EpollInterop.EpollCtlAdd, listenFd, ref ev);
        // Wakeup eventfd — added to epoll so UnbindAsync can unblock epoll_wait without polling.
        _wakeupFd = EpollInterop.EventFd(0, EpollInterop.EfdNonblock | EpollInterop.EfdCloexec);
        if (_wakeupFd >= 0)
        {
            EpollEvent wakeEv = new()
            {
                Events = EpollInterop.EpollIn | EpollInterop.EpollEdgeTriggered,
                Data = new EpollData { Fd = _wakeupFd }
            };
            EpollInterop.EpollCtl(_epollFd, EpollInterop.EpollCtlAdd, _wakeupFd, ref wakeEv);
        }
        // NUMA-local memory pool shared by all connections accepted on this shard.
        _pool = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? new NumaMemoryPool(_node.NodeId)
            : null;
        _epollThread = new Thread(EpollLoop)
        {
            IsBackground = true,
            Name = $"epoll poll node={_node.NodeId}"
        };
        _epollThread.Start();
        LogListenerBound(_logger, _boundEndPoint);
        return ValueTask.CompletedTask;
    }
    /// <inheritdoc />
    public async ValueTask<IConnection?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _acceptQueue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
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
        _acceptQueue.Writer.TryComplete();
        WakeEpollThread();
        if (_epollThread is not null)
        {
            await Task.Run(() => _epollThread.Join(TimeSpan.FromSeconds(5)))
                .ConfigureAwait(false);
        }
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await UnbindAsync().ConfigureAwait(false);
        if (_epollFd >= 0)
        {
            EpollInterop.Close(_epollFd);
            _epollFd = -1;
        }
        if (_wakeupFd >= 0)
        {
            EpollInterop.Close(_wakeupFd);
            _wakeupFd = -1;
        }
        _pool?.Dispose();
        _listenSocket?.Dispose();
        _cts.Dispose();
    }
    // Runs on a dedicated OS thread pinned to the owning NUMA node.
    // Uses timeout=-1 (infinite wait) — the thread only wakes when the OS delivers an
    // EPOLLIN event on the listen socket or when UnbindAsync writes to the wakeup eventfd.
    private unsafe void EpollLoop()
    {
        try { _topology.PinCurrentThreadToNode(_node); }
        catch (Exception ex) { LogPinFailed(_logger, _node.NodeId, ex); }
        const int maxEvents = 256;
        EpollEvent* events = stackalloc EpollEvent[maxEvents];
        while (!_cts.IsCancellationRequested)
        {
            int n = EpollInterop.EpollWait(_epollFd, events, maxEvents, timeout: -1 /* infinite */);
            if (n < 0)
            {
                int err = Marshal.GetLastPInvokeError();
                if (err == 4 /* EINTR */)
                {
                    continue;
                }
                LogEpollWaitFailed(_logger, err);
                break;
            }
            for (int i = 0; i < n; i++)
            {
                if ((events[i].Events & EpollInterop.EpollIn) == 0)
                {
                    continue;
                }
                if (events[i].Data.Fd == _wakeupFd)
                {
                    // Drain the eventfd counter (8-byte read) so the fd doesn't stay ready.
                    ulong discard;
                    EpollInterop.Read(_wakeupFd, &discard, sizeof(ulong));
                }
                else
                {
                    AcceptAvailableConnections();
                }
            }
        }
        _acceptQueue.Writer.TryComplete();
    }
    // Writes 1 to the wakeup eventfd, unblocking epoll_wait on the poll thread.
    private unsafe void WakeEpollThread()
    {
        if (_wakeupFd < 0)
        {
            return;
        }
        ulong one = 1;
        EpollInterop.Write(_wakeupFd, &one, sizeof(ulong));
    }
    private void AcceptAvailableConnections()
    {
        while (true)
        {
            try
            {
                Socket accepted = _listenSocket!.Accept();
                accepted.NoDelay = true;
                accepted.Blocking = false;
                EpollConnection connection = new(accepted, _pool, _logger);
                if (!_acceptQueue.Writer.TryWrite(connection))
                {
                    connection.DisposeAsync().AsTask().ContinueWith(
                        static t => t.Exception?.Handle(_ => true),
                        TaskScheduler.Default);
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                break; // No more pending connections
            }
            catch (Exception ex)
            {
                LogAcceptError(_logger, ex);
                break;
            }
        }
    }
    [LoggerMessage(Level = LogLevel.Information, Message = "EpollListener bound to {EndPoint}.")]
    private static partial void LogListenerBound(ILogger logger, EndPoint endPoint);
    [LoggerMessage(Level = LogLevel.Error, Message = "epoll_wait failed: errno {Errno}.")]
    private static partial void LogEpollWaitFailed(ILogger logger, int errno);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Accept error.")]
    private static partial void LogAcceptError(ILogger logger, Exception ex);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to pin epoll poll thread to NUMA node {NodeId}.")]
    private static partial void LogPinFailed(ILogger logger, int nodeId, Exception ex);
}
