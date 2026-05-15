using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NumaSharp.Transport.Epoll;

namespace NumaSharp.Sample.KestrelBenchmark;

// ── Endpoint descriptor ───────────────────────────────────────────────────────

internal sealed record EndpointSpec(
    string Label,
    string Path,
    HttpMethod Method,
    byte[]? RequestBody);

// ── Abstract scenario base ────────────────────────────────────────────────────

internal abstract class KestrelScenario
{
    private static readonly byte[] s_data4k;
    private static readonly byte[] s_data64k;
    private static readonly byte[] s_echoBody512 = new byte[512];
    private static readonly byte[] s_echoBody4k  = new byte[4  * 1024];
    private static readonly byte[] s_echoBody64k = new byte[64 * 1024];

    private static readonly EndpointSpec[] s_endpoints =
    [
        new("GET /ping",           "/ping",    HttpMethod.Get,  null),
        new("GET /data  (4 KB)",   "/data4k",  HttpMethod.Get,  null),
        new("GET /data  (64 KB)",  "/data64k", HttpMethod.Get,  null),
        new("POST /echo (512 B)",  "/echo",    HttpMethod.Post, s_echoBody512),
        new("POST /echo (4 KB)",   "/echo",    HttpMethod.Post, s_echoBody4k),
        new("POST /echo (64 KB)",  "/echo",    HttpMethod.Post, s_echoBody64k),
    ];

    static KestrelScenario()
    {
        s_data4k  = new byte[4  * 1024];
        s_data64k = new byte[64 * 1024];
        Random.Shared.NextBytes(s_data4k);
        Random.Shared.NextBytes(s_data64k);
        Random.Shared.NextBytes(s_echoBody512);
        Random.Shared.NextBytes(s_echoBody4k);
        Random.Shared.NextBytes(s_echoBody64k);
    }

    public abstract string Label { get; }

    public virtual bool IsAvailable => true;

    public virtual string SkipReason => string.Empty;

    /// <summary>Override to attach a NumaSharp transport factory to Kestrel.</summary>
    protected virtual void ConfigureTransport(IWebHostBuilder webHost) { }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static IEnumerable<KestrelScenario> CreateAll(BenchmarkConfig config)
    {
        if (config.RunBaseline)
        {
            yield return new BaselineKestrelScenario();
        }

        if (config.RunEpoll)
        {
            yield return new EpollKestrelScenario();
        }
    }

    // ── Public entry-point ────────────────────────────────────────────────────

    public async Task<List<BenchmarkResult>> RunAsync(
        BenchmarkConfig config,
        int port,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return BuildSkippedResults(SkipReason);
        }

        WebApplication? app = null;
        try
        {
            app = BuildApp(port);
            await app.StartAsync(cancellationToken);
            await Task.Delay(300, cancellationToken);

            using HttpClient client = CreateClient(port, config.Concurrency);

            List<BenchmarkResult> results = [];
            foreach (EndpointSpec ep in s_endpoints)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                BenchmarkResult result = await RunEndpointAsync(client, ep, config, cancellationToken);
                results.Add(result);
            }

            return results;
        }
        catch (PlatformNotSupportedException ex)
        {
            return BuildSkippedResults(ex.Message);
        }
        catch (DllNotFoundException ex)
        {
            return BuildSkippedResults($"native library missing: {ex.Message}");
        }
        finally
        {
            if (app is not null)
            {
                await app.StopAsync(cancellationToken);
                await app.DisposeAsync();
            }
        }
    }

    // ── Server construction ───────────────────────────────────────────────────

    private WebApplication BuildApp(int port)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(Array.Empty<string>());

        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });
            options.Limits.MaxRequestBodySize = 16L * 1024 * 1024;
        });

        ConfigureTransport(builder.WebHost);

        WebApplication app = builder.Build();

        byte[] data4k  = s_data4k;
        byte[] data64k = s_data64k;

        app.MapGet("/ping", static () => "pong");

        app.MapGet("/data4k", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = data4k.Length;
            await ctx.Response.Body.WriteAsync(data4k);
        });

        app.MapGet("/data64k", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = data64k.Length;
            await ctx.Response.Body.WriteAsync(data64k);
        });

        app.MapPost("/echo", static async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength = ctx.Request.ContentLength;
            await ctx.Request.Body.CopyToAsync(ctx.Response.Body);
        });

        return app;
    }

    // ── HTTP client ───────────────────────────────────────────────────────────

    private static HttpClient CreateClient(int port, int concurrency)
    {
        SocketsHttpHandler handler = new()
        {
            MaxConnectionsPerServer = concurrency,
            UseCookies = false,
            UseProxy = false,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri($"http://localhost:{port}"),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    // ── Per-endpoint measurement ──────────────────────────────────────────────

    private async Task<BenchmarkResult> RunEndpointAsync(
        HttpClient client,
        EndpointSpec ep,
        BenchmarkConfig config,
        CancellationToken cancellationToken = default)
    {
        Console.Write($"    {ep.Label,-26}  warmup... ");

        await RunLoadAsync(
            client, ep, config.Concurrency,
            TimeSpan.FromSeconds(config.WarmupSeconds),
            histogram: null,
            cancellationToken);

        Console.Write("measuring... ");

        LatencyHistogram histogram = new();
        Stopwatch sw = Stopwatch.StartNew();

        (long ops, long bytes) = await RunLoadAsync(
            client, ep, config.Concurrency,
            TimeSpan.FromSeconds(config.MeasureSeconds),
            histogram,
            cancellationToken);

        sw.Stop();

        double elapsed = sw.Elapsed.TotalSeconds;
        double rps = ops / elapsed;
        double mbps = bytes / elapsed / (1024.0 * 1024.0);

        Console.WriteLine($"{ops:N0} ops  ({rps:N0} RPS,  {mbps:F1} MB/s)");

        return new BenchmarkResult
        {
            ScenarioLabel = Label,
            EndpointLabel = ep.Label,
            RequestsPerSecond = rps,
            MegabytesPerSecond = mbps,
            P50Ns = histogram.Percentile(50),
            P95Ns = histogram.Percentile(95),
            P99Ns = histogram.Percentile(99),
        };
    }

    // ── Concurrent load runner ────────────────────────────────────────────────

    private static async Task<(long Ops, long Bytes)> RunLoadAsync(
        HttpClient client,
        EndpointSpec ep,
        int concurrency,
        TimeSpan duration,
        LatencyHistogram? histogram,
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenSource linked = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        linked.CancelAfter(duration);
        CancellationToken ct = linked.Token;

        long totalOps = 0;
        long totalBytes = 0;

        Task[] workers = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            workers[i] = Task.Run(async () =>
            {
                using System.IO.MemoryStream drain = new(128 * 1024);

                long localOps = 0;
                long localBytes = 0;

                while (!ct.IsCancellationRequested)
                {
                    drain.SetLength(0);
                    long t0 = Stopwatch.GetTimestamp();
                    try
                    {
                        if (ep.RequestBody is null)
                        {
                            using HttpResponseMessage resp = await client
                                .GetAsync(ep.Path, HttpCompletionOption.ResponseHeadersRead, ct)
                                .ConfigureAwait(false);

                            await resp.Content.CopyToAsync(drain, ct)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            using HttpRequestMessage req = new(ep.Method, ep.Path)
                            {
                                Content = new ByteArrayContent(ep.RequestBody),
                            };

                            using HttpResponseMessage resp = await client
                                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                                .ConfigureAwait(false);

                            await resp.Content.CopyToAsync(drain, ct)
                                .ConfigureAwait(false);
                        }

                        long elapsed = (long)Stopwatch.GetElapsedTime(t0).TotalNanoseconds;
                        histogram?.Record(elapsed);
                        localOps++;
                        localBytes += drain.Length;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (HttpRequestException) { /* transient on server shutdown */ }
                }

                Interlocked.Add(ref totalOps, localOps);
                Interlocked.Add(ref totalBytes, localBytes);
            }, CancellationToken.None);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
        return (totalOps, totalBytes);
    }

    private List<BenchmarkResult> BuildSkippedResults(string reason) =>
        s_endpoints
            .Select(ep => BenchmarkResult.CreateSkipped(Label, ep.Label, reason))
            .ToList();
}

// ── Concrete scenarios ────────────────────────────────────────────────────────

/// <summary>Kestrel with its built-in SocketTransport (baseline).</summary>
internal sealed class BaselineKestrelScenario : KestrelScenario
{
    public override string Label => "Kestrel Default (SocketTransport)";
}

/// <summary>Kestrel backed by NumaSharp epoll transport (Linux only).</summary>
internal sealed class EpollKestrelScenario : KestrelScenario
{
    public override string Label => "NumaSharp Epoll Transport";

    public override bool IsAvailable =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public override string SkipReason => "requires Linux";

    protected override void ConfigureTransport(IWebHostBuilder webHost)
    {
        EpollTransportFactory factory = new(
            new NumaSharp.Scheduling.NumaTaskScheduler());
        webHost.UseNumaSharpTransport(factory);
    }
}
