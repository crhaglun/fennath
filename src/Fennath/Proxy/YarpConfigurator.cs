using Fennath.Certificates;
using Fennath.Configuration;
using Fennath.Discovery;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Proxy;

/// <summary>
/// Configures the YARP reverse proxy pipeline and wires up route discovery
/// and certificate watching. DNS/ACME management runs in the sidecar container
/// (see ADR-014).
/// </summary>
public static class YarpConfigurator
{
    /// <summary>
    /// Adds YARP reverse proxy with Fennath's dynamic route management.
    /// </summary>
    public static IServiceCollection AddFennathProxy(this IServiceCollection services,
        IConfiguration configuration)
    {
        // YARP
        var inMemoryConfig = new InMemoryConfigProvider([], []);
        services.AddSingleton(inMemoryConfig);
        services.AddSingleton<IProxyConfigProvider>(inMemoryConfig);
        services.AddReverseProxy();

        // Route discovery — Docker labels
        services.AddSingleton<DockerRouteDiscovery>();
        services.AddSingleton<IRouteDiscovery>(sp => sp.GetRequiredService<DockerRouteDiscovery>());
        services.AddHostedService(sp => sp.GetRequiredService<DockerRouteDiscovery>());

        services.AddSingleton(sp =>
        {
            var sources = sp.GetServices<IRouteDiscovery>();
            var config = sp.GetRequiredService<IOptions<FennathConfig>>().Value;
            return new RouteAggregator(
                sources,
                sp.GetRequiredService<InMemoryConfigProvider>(),
                config.EffectiveDomain,
                sp.GetRequiredService<ILogger<RouteAggregator>>());
        });

        // Certificate store — reads certs written by the sidecar
        services.AddSingleton<CertificateStore>();
        services.AddHostedService<CertificateFileWatcher>();

        // Route file writer — publishes discovered subdomains for the sidecar
        services.AddHostedService<RouteFileWriter>();

        return services;
    }
}
