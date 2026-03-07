using Fennath.Configuration;
using Fennath.Discovery;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Proxy;

/// <summary>
/// Configures the YARP reverse proxy pipeline and wires up route discovery.
/// </summary>
public static class YarpConfigurator
{
    /// <summary>
    /// Adds YARP reverse proxy with Fennath's dynamic route management.
    /// </summary>
    public static IServiceCollection AddFennathProxy(this IServiceCollection services)
    {
        var inMemoryConfig = new InMemoryConfigProvider([], []);

        services.AddSingleton(inMemoryConfig);
        services.AddSingleton<IProxyConfigProvider>(inMemoryConfig);
        services.AddReverseProxy();

        services.AddSingleton<IRouteDiscovery, StaticRouteDiscovery>();

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

        return services;
    }
}
