using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
namespace NumaSharp.Transport.Epoll;
/// <summary>Extension methods to register the NumaSharp Kestrel transport.</summary>
public static class NumaSharpKestrelExtensions
{
    /// <summary>
    /// Configures Kestrel to use the NumaSharp epoll transport.
    /// Call this on the <see cref="IWebHostBuilder"/> to replace Kestrel's
    /// default socket transport with NumaSharp's high-performance epoll transport.
    /// </summary>
    /// <param name="hostBuilder">The web host builder.</param>
    /// <param name="transportFactory">
    /// The NumaSharp transport factory (e.g. <see cref="EpollTransportFactory"/>).
    /// </param>
    /// <returns>The same <paramref name="hostBuilder"/> for chaining.</returns>
    public static IWebHostBuilder UseNumaSharpTransport(
        this IWebHostBuilder hostBuilder,
        ITransportFactory transportFactory)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);
        ArgumentNullException.ThrowIfNull(transportFactory);
        return hostBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<IConnectionListenerFactory>(sp =>
                new NumaSharpConnectionListenerFactory(
                    transportFactory,
                    sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        });
    }
}
