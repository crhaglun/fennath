using Fennath.Configuration;
using Fennath.Discovery;
using Fennath.Sidecar.Dns;
using Fennath.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Tests.Helpers;

/// <summary>
/// Creates a test Fennath host wired with real production services
/// (YARP, ProxyMetricsMiddleware, DnsReconciliationService)
/// and test doubles at I/O boundaries (InMemoryConfigProvider, FakeDnsProvider).
///
/// Route configuration is injected via InMemoryConfigProvider rather than
/// file-based config (mirroring how the sidecar writes YARP config in production).
/// </summary>
public static class FennathTestHost
{
    public static async Task<FennathTestContext> CreateAsync(
        params (string Subdomain, string Backend)[] routes)
    {
        var fakeDns = new FakeDnsProvider();
        const string testDomain = "example.com";

        var (yarpRoutes, yarpClusters) = BuildYarpConfig(testDomain, routes);
        var inMemoryConfig = new InMemoryConfigProvider(yarpRoutes, yarpClusters);

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    // Configuration with valid test defaults
                    services.AddOptions<FennathConfig>().Configure(config =>
                    {
                        config.Domain = testDomain;
                        config.Dns.Loopia.Username = "test";
                        config.Dns.Loopia.Password = "test";
                        config.Certificates.Email = "test@example.com";
                    });

                    // YARP reverse proxy — test uses InMemoryConfigProvider
                    services.AddSingleton(inMemoryConfig);
                    services.AddSingleton<IProxyConfigProvider>(inMemoryConfig);
                    services.AddReverseProxy();

                    // DNS — test double + real reconciliation service
                    services.AddSingleton(fakeDns);
                    services.AddSingleton<IDnsProvider>(sp => sp.GetRequiredService<FakeDnsProvider>());
                    services.AddSingleton<DnsCommandChannel>();
                    services.AddHostedService<DnsReconciliationService>();

                    // Metrics — real
                    services.AddSingleton<FennathMetrics>();

                    services.AddHealthChecks();
                });

                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHealthChecks("/healthz");
                        endpoints.MapReverseProxy(p =>
                            p.UseMiddleware<ProxyMetricsMiddleware>());
                    });
                });
            })
            .Build();

        await host.StartAsync();

        return new FennathTestContext(host, inMemoryConfig, fakeDns);
    }

    /// <summary>
    /// Builds YARP route and cluster configuration from subdomain/backend tuples.
    /// </summary>
    internal static (List<RouteConfig> Routes, List<ClusterConfig> Clusters) BuildYarpConfig(
        string domain, (string Subdomain, string Backend)[] routes)
    {
        var yarpRoutes = new List<RouteConfig>();
        var yarpClusters = new List<ClusterConfig>();

        foreach (var (subdomain, backend) in routes)
        {
            var routeId = $"route-{subdomain}";
            var clusterId = $"cluster-{subdomain}";
            var host = subdomain == DiscoveredRoute.ApexMarker
                ? domain
                : $"{subdomain}.{domain}";

            yarpRoutes.Add(new RouteConfig
            {
                RouteId = routeId,
                ClusterId = clusterId,
                Match = new RouteMatch { Hosts = [host] }
            });

            yarpClusters.Add(new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["default"] = new DestinationConfig { Address = backend }
                }
            });
        }

        return (yarpRoutes, yarpClusters);
    }
}
