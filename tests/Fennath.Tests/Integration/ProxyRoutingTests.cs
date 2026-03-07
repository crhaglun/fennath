using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Fennath.Configuration;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Tests.Integration;

public class ProxyRoutingTests : IAsyncDisposable
{
    private IHost? _backendHost;
    private string _backendUrl = "";

    [Before(Test)]
    public async Task SetUp()
    {
        _backendHost = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls("http://127.0.0.1:0");
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/", () => "hello from backend");
                        endpoints.MapGet("/api/health", () => "ok");
                    });
                });
            })
            .Build();

        await _backendHost.StartAsync();

        var addresses = _backendHost.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>();
        _backendUrl = addresses!.Addresses.First();
    }

    [After(Test)]
    public async Task TearDown()
    {
        if (_backendHost is not null)
            await _backendHost.StopAsync();
        _backendHost?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_backendHost is not null)
            await _backendHost.StopAsync();
        _backendHost?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task Request_with_matching_host_header_is_proxied_to_backend()
    {
        using var fennath = await CreateFennathTestHost(
            new RouteEntry { Subdomain = "grafana", Backend = _backendUrl });

        var client = fennath.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "grafana.example.com";

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).IsEqualTo("hello from backend");
    }

    [Test]
    public async Task Request_with_unknown_host_returns_not_found()
    {
        using var fennath = await CreateFennathTestHost(
            new RouteEntry { Subdomain = "grafana", Backend = _backendUrl });

        var client = fennath.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "unknown.example.com";

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Healthz_endpoint_returns_healthy()
    {
        using var fennath = await CreateFennathTestHost(
            new RouteEntry { Subdomain = "grafana", Backend = _backendUrl });

        var client = fennath.GetTestClient();

        var response = await client.GetAsync("/healthz");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Multiple_routes_proxy_to_correct_backends()
    {
        using var backend2Host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls("http://127.0.0.1:0");
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/", () => "hello from backend2");
                    });
                });
            })
            .Build();

        await backend2Host.StartAsync();
        var backend2Url = backend2Host.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        using var fennath = await CreateFennathTestHost(
            new RouteEntry { Subdomain = "grafana", Backend = _backendUrl },
            new RouteEntry { Subdomain = "git", Backend = backend2Url });

        var client = fennath.GetTestClient();

        var req1 = new HttpRequestMessage(HttpMethod.Get, "/");
        req1.Headers.Host = "grafana.example.com";
        var resp1 = await client.SendAsync(req1);
        await Assert.That(await resp1.Content.ReadAsStringAsync()).IsEqualTo("hello from backend");

        var req2 = new HttpRequestMessage(HttpMethod.Get, "/");
        req2.Headers.Host = "git.example.com";
        var resp2 = await client.SendAsync(req2);
        await Assert.That(await resp2.Content.ReadAsStringAsync()).IsEqualTo("hello from backend2");

        await backend2Host.StopAsync();
    }

    [Test]
    public async Task Apex_route_matches_bare_domain()
    {
        using var fennath = await CreateFennathTestHost(
            new RouteEntry { Subdomain = "@", Backend = _backendUrl });

        var client = fennath.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "example.com";

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).IsEqualTo("hello from backend");
    }

    private static async Task<IHost> CreateFennathTestHost(params RouteEntry[] routes)
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
                        endpoints.MapReverseProxy();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        return host;
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

file static class HostExtensions
{
    public static HttpClient GetTestClient(this IHost host)
    {
        return host.Services.GetRequiredService<IServer>() is TestServer testServer
            ? testServer.CreateClient()
            : throw new InvalidOperationException("Host is not using TestServer");
    }
}
