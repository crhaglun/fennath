using Fennath.Configuration;
using Fennath.Discovery;
using Fennath.Dns;
using Fennath.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Tests.Helpers;

/// <summary>
/// Creates a test Fennath host wired with real production services
/// (RouteAggregator, ProxyMetricsMiddleware, DnsReconciliationService)
/// and test doubles at I/O boundaries (FakeRouteDiscovery, FakeDnsProvider).
/// </summary>
public static class FennathTestHost
{
    public static async Task<FennathTestContext> CreateAsync(
        params (string Subdomain, string Backend)[] routes)
    {
        var fakeDiscovery = new FakeRouteDiscovery();
        var fakeDns = new FakeDnsProvider();

        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    // Configuration with valid test defaults
                    services.AddOptions<FennathConfig>().Configure(config =>
                    {
                        config.Domain = "example.com";
                        config.Dns.Loopia.Username = "test";
                        config.Dns.Loopia.Password = "test";
                        config.Certificates.Email = "test@example.com";
                    });

                    // YARP reverse proxy
                    var inMemoryConfig = new InMemoryConfigProvider([], []);
                    services.AddSingleton(inMemoryConfig);
                    services.AddSingleton<IProxyConfigProvider>(inMemoryConfig);
                    services.AddReverseProxy();

                    // Route discovery — test double
                    services.AddSingleton(fakeDiscovery);
                    services.AddSingleton<IRouteDiscovery>(sp => sp.GetRequiredService<FakeRouteDiscovery>());

                    // Route aggregation — real production code
                    services.AddSingleton(sp => new RouteAggregator(
                        sp.GetServices<IRouteDiscovery>(),
                        sp.GetRequiredService<InMemoryConfigProvider>(),
                        sp.GetRequiredService<IOptions<FennathConfig>>().Value.Domain,
                        sp.GetRequiredService<ILogger<RouteAggregator>>()));

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

        // Set initial routes before starting (RouteAggregator subscribes in constructor)
        fakeDiscovery.SetRoutes(routes
            .Select(r => new DiscoveredRoute(r.Subdomain, r.Backend, "test"))
            .ToArray());

        // Eagerly resolve RouteAggregator to trigger initial route building
        _ = host.Services.GetRequiredService<RouteAggregator>();

        await host.StartAsync();

        return new FennathTestContext(host, fakeDiscovery, fakeDns);
    }
}
