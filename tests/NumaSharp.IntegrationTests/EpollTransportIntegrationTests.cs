using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using NumaSharp.Transport.Epoll;
using NumaSharp.Scheduling;
using NumaSharp.Transport;

namespace NumaSharp.IntegrationTests;

/// <summary>
/// End-to-end integration tests for the NumaSharp epoll transport.
/// All tests are Linux-only and skipped gracefully on other platforms.
/// </summary>
public sealed class EpollTransportIntegrationTests
{
    private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    [Test]
    public async Task BindAsync_ThrowsOnNonLinux()
    {
        if (IsLinux) { return; } // skip on Linux

        using NumaTaskScheduler scheduler = new();
        EpollTransportFactory factory = new(scheduler);

        await Assert.That(async () => { await factory.BindAsync(new IPEndPoint(IPAddress.Loopback, 0)); })
            .Throws<PlatformNotSupportedException>();
    }

    [Test]
    public async Task BindAsync_SucceedsOnLinux()
    {
        if (!IsLinux) { return; }

        using NumaTaskScheduler scheduler = new();
        EpollTransportFactory factory = new(scheduler, new EpollTransportOptions { ListenerShards = 1 });

        await using IListener listener = await factory.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));

        await Assert.That(listener).IsNotNull();
        await Assert.That(listener.EndPoint).IsNotNull();
    }

    [Test]
    public async Task SingleClient_CanConnectAndExchange_Data()
    {
        if (!IsLinux) { return; }

        using NumaTaskScheduler scheduler = new();
        EpollTransportFactory factory = new(scheduler, new EpollTransportOptions { ListenerShards = 1 });

        await using IListener listener = await factory.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        IPEndPoint serverEndPoint = (IPEndPoint)listener.EndPoint;

        // Connect client socket
        using Socket clientSocket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await clientSocket.ConnectAsync(serverEndPoint);

        // Accept the server-side connection
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        IConnection? serverConn = await listener.AcceptAsync(cts.Token);

        await Assert.That(serverConn).IsNotNull();

        // Send data from client
        byte[] sent = [0x01, 0x02, 0x03, 0x04, 0x05];
        await clientSocket.SendAsync(sent, SocketFlags.None);

        // Read data on server side via pipeline
        System.IO.Pipelines.ReadResult result = await serverConn!.Transport.Input
            .ReadAsync(cts.Token);

        await Assert.That(result.Buffer.Length).IsGreaterThanOrEqualTo(sent.Length);
        serverConn.Transport.Input.AdvanceTo(result.Buffer.End);

        // Unwind
        await listener.UnbindAsync();
        await serverConn.DisposeAsync();
    }

    [Test]
    public async Task MultiShard_BindAndUnbind_Succeeds()
    {
        if (!IsLinux) { return; }

        using NumaTaskScheduler scheduler = new();
        int shards = Math.Max(2, scheduler.NodeCount);
        EpollTransportFactory factory = new(scheduler, new EpollTransportOptions { ListenerShards = shards });

        await using IListener listener = await factory.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));

        await Assert.That(listener).IsNotNull();
        await listener.UnbindAsync();
    }

    [Test]
    public async Task Listener_EndPoint_IsValidBoundAddress()
    {
        if (!IsLinux) { return; }

        using NumaTaskScheduler scheduler = new();
        EpollTransportFactory factory = new(scheduler, new EpollTransportOptions { ListenerShards = 1 });

        await using IListener listener = await factory.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        IPEndPoint ep = (IPEndPoint)listener.EndPoint;

        await Assert.That(ep.Port).IsGreaterThan(0);
        await Assert.That(ep.Address).IsEqualTo(IPAddress.Loopback);
    }

    [Test]
    public async Task DisposeAsync_DoesNotThrow()
    {
        if (!IsLinux) { return; }

        using NumaTaskScheduler scheduler = new();
        EpollTransportFactory factory = new(scheduler, new EpollTransportOptions { ListenerShards = 1 });

        IListener listener = await factory.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));

        await Assert.That(() => listener.DisposeAsync().AsTask()).ThrowsNothing();
    }
}
