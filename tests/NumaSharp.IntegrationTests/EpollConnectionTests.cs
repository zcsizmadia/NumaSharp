using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using NumaSharp.Scheduling;
using NumaSharp.Transport;
using NumaSharp.Transport.Epoll;

namespace NumaSharp.IntegrationTests;

/// <summary>
/// Extended integration tests for the epoll transport: connection lifecycle,
/// bidirectional data exchange, concurrent clients, and error paths.
/// All tests are Linux-only and gracefully skip on other platforms.
/// </summary>
public sealed class EpollConnectionTests
{
    private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    private static async Task<(IListener Listener, IPEndPoint Endpoint)> BindAsync(
        NumaTaskScheduler scheduler, int shards = 1)
    {
        EpollTransportFactory factory = new(scheduler, new EpollTransportOptions
        {
            ListenerShards = shards,
        });
        IListener listener = await factory.BindAsync(new IPEndPoint(IPAddress.Loopback, 0));
        return (listener, (IPEndPoint)listener.EndPoint);
    }

    [Test]
    public async Task BidirectionalEcho_SmallPayload_RoundTrips()
    {
        if (!IsLinux)
        {
            return;
        }

        using NumaTaskScheduler scheduler = new();
        (IListener listener, IPEndPoint ep) = await BindAsync(scheduler);
        await using (listener)
        {
            using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(ep);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
            IConnection? conn = await listener.AcceptAsync(cts.Token);
            await Assert.That(conn).IsNotNull();

            byte[] payload = Encoding.ASCII.GetBytes("hello-numa");
            await client.SendAsync(payload, SocketFlags.None);

            // Read from pipeline
            System.IO.Pipelines.ReadResult rr = await conn!.Transport.Input.ReadAsync(cts.Token);
            byte[] rxBytes = new byte[(int)rr.Buffer.Length];
            int copyPos = 0;
            foreach (ReadOnlyMemory<byte> segment in rr.Buffer)
            {
                segment.Span.CopyTo(rxBytes.AsSpan(copyPos));
                copyPos += segment.Length;
            }

            string received = Encoding.ASCII.GetString(rxBytes, 0, payload.Length);
            conn.Transport.Input.AdvanceTo(rr.Buffer.End);

            await Assert.That(received).IsEqualTo("hello-numa");

            await conn.DisposeAsync();
        }
    }

    [Test]
    public async Task ServerWrite_ClientReceives_Data()
    {
        if (!IsLinux)
        {
            return;
        }

        using NumaTaskScheduler scheduler = new();
        (IListener listener, IPEndPoint ep) = await BindAsync(scheduler);
        await using (listener)
        {
            using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(ep);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
            IConnection? conn = await listener.AcceptAsync(cts.Token);

            // Write from server pipeline
            byte[] toSend = Encoding.ASCII.GetBytes("server-response");
            System.IO.Pipelines.PipeWriter writer = conn!.Transport.Output;
            await writer.WriteAsync(toSend.AsMemory(), cts.Token);
            await writer.FlushAsync(cts.Token);

            // Read on client
            byte[] buf = new byte[64];
            client.ReceiveTimeout = 5000;
            int read = client.Receive(buf);

            string received = Encoding.ASCII.GetString(buf, 0, read);
            await Assert.That(received).IsEqualTo("server-response");

            await conn.DisposeAsync();
        }
    }

    [Test]
    public async Task LargePayload_1MB_TransfersCorrectly()
    {
        if (!IsLinux)
        {
            return;
        }

        using NumaTaskScheduler scheduler = new();
        (IListener listener, IPEndPoint ep) = await BindAsync(scheduler);
        await using (listener)
        {
            using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(ep);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
            IConnection? conn = await listener.AcceptAsync(cts.Token);

            byte[] payload = new byte[1024 * 1024]; // 1 MB
            Random.Shared.NextBytes(payload);

            // Send async from background thread
            Task sendTask = Task.Run(() => client.Send(payload, SocketFlags.None), cts.Token);

            // Accumulate on server side
            long totalRead = 0;
            while (totalRead < payload.Length)
            {
                System.IO.Pipelines.ReadResult rr = await conn!.Transport.Input.ReadAsync(cts.Token);
                totalRead += rr.Buffer.Length;
                conn.Transport.Input.AdvanceTo(rr.Buffer.End);
                if (rr.IsCompleted)
                {
                    break;
                }
            }

            await sendTask;
            await Assert.That(totalRead).IsGreaterThanOrEqualTo(payload.Length);

            await conn!.DisposeAsync();
        }
    }

    [Test]
    public async Task ConcurrentClients_AllAccepted()
    {
        if (!IsLinux)
        {
            return;
        }

        using NumaTaskScheduler scheduler = new();
        (IListener listener, IPEndPoint ep) = await BindAsync(scheduler);
        await using (listener)
        {
            const int clientCount = 10;
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));

            // Connect all clients
            Socket[] clients = new Socket[clientCount];
            for (int i = 0; i < clientCount; i++)
            {
                clients[i] = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await clients[i].ConnectAsync(ep, cts.Token);
            }

            // Accept all connections
            IConnection[] conns = new IConnection[clientCount];
            for (int i = 0; i < clientCount; i++)
            {
                IConnection? conn = await listener.AcceptAsync(cts.Token);
                await Assert.That(conn).IsNotNull();
                conns[i] = conn!;
            }

            await Assert.That(conns.Length).IsEqualTo(clientCount);

            // Cleanup
            foreach (IConnection conn in conns)
            {
                await conn.DisposeAsync();
            }

            foreach (Socket s in clients)
            {
                s.Dispose();
            }
        }
    }

    [Test]
    public async Task ConnectionId_IsUniquePerConnection()
    {
        if (!IsLinux)
        {
            return;
        }

        using NumaTaskScheduler scheduler = new();
        (IListener listener, IPEndPoint ep) = await BindAsync(scheduler);
        await using (listener)
        {
            const int count = 5;
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
            HashSet<string> ids = [];
            Socket[] clients = new Socket[count];

            for (int i = 0; i < count; i++)
            {
                clients[i] = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await clients[i].ConnectAsync(ep, cts.Token);
            }

            for (int i = 0; i < count; i++)
            {
                IConnection? conn = await listener.AcceptAsync(cts.Token);
                ids.Add(conn!.ConnectionId);
                await conn.DisposeAsync();
            }

            await Assert.That(ids.Count).IsEqualTo(count);

            foreach (Socket s in clients)
            {
                s.Dispose();
            }
        }
    }

    [Test]
    public async Task LocalEndPoint_MatchesListenerPort()
    {
        if (!IsLinux)
        {
            return;
        }

        using NumaTaskScheduler scheduler = new();
        (IListener listener, IPEndPoint ep) = await BindAsync(scheduler);
        await using (listener)
        {
            using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(ep);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            IConnection? conn = await listener.AcceptAsync(cts.Token);

            IPEndPoint? local = conn!.LocalEndPoint as IPEndPoint;
            await Assert.That(local).IsNotNull();
            await Assert.That(local!.Port).IsEqualTo(ep.Port);

            await conn.DisposeAsync();
        }
    }

    [Test]
    public async Task RemoteEndPoint_MatchesClientPort()
    {
        if (!IsLinux)
        {
            return;
        }

        using NumaTaskScheduler scheduler = new();
        (IListener listener, IPEndPoint ep) = await BindAsync(scheduler);
        await using (listener)
        {
            using Socket client = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(ep);
            int clientPort = ((IPEndPoint)client.LocalEndPoint!).Port;

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            IConnection? conn = await listener.AcceptAsync(cts.Token);

            IPEndPoint? remote = conn!.RemoteEndPoint as IPEndPoint;
            await Assert.That(remote).IsNotNull();
            await Assert.That(remote!.Port).IsEqualTo(clientPort);

            await conn.DisposeAsync();
        }
    }

    [Test]
    public async Task MultiShard_MultipleClients_AcceptedAcrossShards()
    {
        if (!IsLinux)
        {
            return;
        }

        using NumaTaskScheduler scheduler = new();
        int shards = Math.Min(2, scheduler.NodeCount);
        (IListener listener, IPEndPoint ep) = await BindAsync(scheduler, shards);
        await using (listener)
        {
            const int clientCount = 8;
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));
            Socket[] clients = new Socket[clientCount];

            for (int i = 0; i < clientCount; i++)
            {
                clients[i] = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await clients[i].ConnectAsync(ep, cts.Token);
            }

            List<IConnection> conns = [];
            for (int i = 0; i < clientCount; i++)
            {
                IConnection? conn = await listener.AcceptAsync(cts.Token);
                await Assert.That(conn).IsNotNull();
                conns.Add(conn!);
            }

            await Assert.That(conns.Count).IsEqualTo(clientCount);

            foreach (IConnection c in conns)
            {
                await c.DisposeAsync();
            }

            foreach (Socket s in clients)
            {
                s.Dispose();
            }
        }
    }
}
