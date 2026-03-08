using Fennath.Certificates;
using Fennath.Configuration;
using Fennath.Discovery;
using Fennath.Dns;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Proxy;

/// <summary>
/// Configures the YARP reverse proxy pipeline and wires up route discovery,
/// DNS management, and certificate services.
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
                config.Domain,
                sp.GetRequiredService<ILogger<RouteAggregator>>());
        });

        // DNS
        services.AddHttpClient<LoopiaDnsProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddSingleton<IDnsProvider>(sp => sp.GetRequiredService<LoopiaDnsProvider>());
        services.AddHttpClient<PublicIpResolver>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<PublicIpResolver>();
        services.AddSingleton<IpMonitorService>();
        services.AddHostedService(sp => sp.GetRequiredService<IpMonitorService>());
        services.AddHostedService<DnsReconciliationService>();

        // Certificates
        services.AddSingleton<CertificateStore>();
        services.AddSingleton<AcmeService>();
        services.AddHostedService<CertificateRenewalService>();

        return services;
    }
}
