using Fennath.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Tests.Helpers;

/// <summary>
/// Creates a test Fennath host with YARP configured from route tuples.
/// </summary>
public static class FennathTestHost
{
    public static async Task<IHost> CreateAsync(
        params (string Subdomain, string Backend)[] routes)
    {
        return await CreateAsync(withMetrics: false, routes);
    }

    public static async Task<IHost> CreateWithMetricsAsync(
        params (string Subdomain, string Backend)[] routes)
    {
        return await CreateAsync(withMetrics: true, routes);
    }

    private static async Task<IHost> CreateAsync(
        bool withMetrics, (string Subdomain, string Backend)[] routes)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    var inMemoryConfig = new InMemoryConfigProvider([], []);
                    services.AddSingleton(inMemoryConfig);
                    services.AddSingleton<IProxyConfigProvider>(inMemoryConfig);
                    services.AddReverseProxy();
                    services.AddHealthChecks();

                    if (withMetrics)
                        services.AddSingleton<FennathMetrics>();

                    var yarpRoutes = routes.Select(r => new RouteConfig
                    {
                        RouteId = $"route-{r.Subdomain}",
                        ClusterId = $"cluster-{r.Subdomain}",
                        Match = new RouteMatch
                        {
                            Hosts = [r.Subdomain == "@" ? "example.com" : $"{r.Subdomain}.example.com"]
                        }
                    }).ToList();

                    var yarpClusters = routes.Select(r => new ClusterConfig
                    {
                        ClusterId = $"cluster-{r.Subdomain}",
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            ["default"] = new DestinationConfig { Address = r.Backend }
                        }
                    }).ToList();

                    services.AddHostedService(sp =>
                        new ConfigApplier(sp.GetRequiredService<InMemoryConfigProvider>(), yarpRoutes, yarpClusters));
                });

                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHealthChecks("/healthz");
                        if (withMetrics)
                            endpoints.MapReverseProxy(p => p.UseMiddleware<ProxyMetricsMiddleware>());
                        else
                            endpoints.MapReverseProxy();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return host;
    }

    public static HttpClient GetTestClient(this IHost host)
    {
        return host.Services.GetRequiredService<IServer>() is TestServer testServer
            ? testServer.CreateClient()
            : throw new InvalidOperationException("Host is not using TestServer");
    }

    private sealed class ConfigApplier(
        InMemoryConfigProvider provider,
        List<RouteConfig> routes,
        List<ClusterConfig> clusters) : IHostedService
    {
        public Task StartAsync(CancellationToken ct)
        {
            provider.Update(routes, clusters);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
