using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
namespace NumaSharp.Transport.Epoll;
/// <summary>
/// A managed epoll-based TCP connection backed by <see cref="System.IO.Pipelines"/>.
/// </summary>
internal sealed partial class EpollConnection : ConnectionContext
{
    private static long s_nextId;
    private readonly Socket _socket;
    private readonly ILogger _logger;
    private readonly Pipe _recvPipe;
    private readonly Pipe _sendPipe;
    private readonly DuplexPipe _duplexPipe;
    private readonly string _connectionId;
    private bool _disposed;
    public override string ConnectionId => _connectionId;
    public override EndPoint LocalEndPoint =>
        _socket.LocalEndPoint ?? new IPEndPoint(IPAddress.Any, 0);
    public override EndPoint RemoteEndPoint =>
        _socket.RemoteEndPoint ?? new IPEndPoint(IPAddress.Any, 0);
    public override IDuplexPipe Transport => _duplexPipe;
    internal EpollConnection(Socket socket, MemoryPool<byte>? memoryPool, ILogger logger)
    {
        _socket = socket;
        _logger = logger;
        _connectionId = $"epoll-{Interlocked.Increment(ref s_nextId)}";
        // readerScheduler=Inline: Kestrel's ReadAsync continuation runs inline on the
        // socket-callback thread that just wrote received data, skipping a ThreadPool dispatch
        // per request. EpollConnection's recv loop uses Socket.ReceiveAsync (standard .NET
        // async), not a dedicated epoll poll thread, so the traditional deadlock concern
        // does not apply here. Both schedulers Inline reduces latency for ping-sized payloads.
        // writerScheduler=Inline: back-pressure relief on the consuming thread is safe and rare.
        PipeOptions recvPipeOptions = new(
            pool: memoryPool,
            readerScheduler: PipeScheduler.Inline,
            writerScheduler: PipeScheduler.Inline,
            pauseWriterThreshold: 1024 * 1024,
            resumeWriterThreshold: 512 * 1024,
            useSynchronizationContext: false,
            minimumSegmentSize: 4096);
        // readerScheduler=Inline: SendLoopAsync resumes on the thread that flushed
        // the response (Kestrel's handler thread), skipping a ThreadPool dispatch
        // on every response and reducing P99 tail latency for small payloads.
        // This is safe because SendLoopAsync does not call back into epoll internals.
        PipeOptions sendPipeOptions = new(
            pool: memoryPool,
            readerScheduler: PipeScheduler.Inline,
            writerScheduler: PipeScheduler.Inline,
            pauseWriterThreshold: 1024 * 1024,
            resumeWriterThreshold: 512 * 1024,
            useSynchronizationContext: false,
            minimumSegmentSize: 16384);
        _recvPipe = new Pipe(recvPipeOptions);
        _sendPipe = new Pipe(sendPipeOptions);
        _duplexPipe = new DuplexPipe(_recvPipe.Reader, _sendPipe.Writer);
        _ = ReceiveLoopAsync();
        _ = SendLoopAsync();
    }
    private async Task ReceiveLoopAsync()
    {
        PipeWriter writer = _recvPipe.Writer;
        try
        {
            while (true)
            {
                Memory<byte> buffer = writer.GetMemory(4096);
                int received = await _socket
                    .ReceiveAsync(buffer, SocketFlags.None)
                    .ConfigureAwait(false);
                if (received == 0)
                {
                    break;
                }
                writer.Advance(received);
                FlushResult flush = await writer.FlushAsync().ConfigureAwait(false);
                if (flush.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            writer.Complete(ex);
            LogRecvLoopEnded(_logger, ex, _connectionId);
            SignalClosed();
            return;
        }
        writer.Complete();
        SignalClosed();
    }
    private async Task SendLoopAsync()
    {
        PipeReader reader = _sendPipe.Reader;
        try
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync().ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;
                if (buffer.IsEmpty && result.IsCompleted)
                {
                    break;
                }
                foreach (ReadOnlyMemory<byte> segment in buffer)
                {
                    await _socket.SendAsync(segment, SocketFlags.None).ConfigureAwait(false);
                }
                reader.AdvanceTo(buffer.End);
                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogSendLoopEnded(_logger, ex, _connectionId);
        }
        finally
        {
            reader.Complete();
        }
    }
    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _socket.Dispose();
        _recvPipe.Writer.Complete();
        _sendPipe.Writer.Complete();
        await base.DisposeAsync().ConfigureAwait(false);
    }
    private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input => input;
        public PipeWriter Output => output;
    }
    [LoggerMessage(Level = LogLevel.Debug, Message = "Connection {Id}: recv loop ended.")]
    private static partial void LogRecvLoopEnded(ILogger logger, Exception ex, string id);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Connection {Id}: send loop ended.")]
    private static partial void LogSendLoopEnded(ILogger logger, Exception ex, string id);
}
