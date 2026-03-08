using Fennath.Discovery;
using Fennath.Dns;
using Fennath.Telemetry;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fennath.Tests.Helpers;

/// <summary>
/// Wraps a test Fennath host, exposing test doubles for assertions.
/// </summary>
public sealed class FennathTestContext : IAsyncDisposable
{
    private readonly IHost _host;

    public FakeRouteDiscovery RouteDiscovery { get; }
    public FakeDnsProvider DnsProvider { get; }
    public FennathMetrics Metrics { get; }
    public DnsCommandChannel DnsChannel { get; }
    public RouteAggregator RouteAggregator { get; }

    internal FennathTestContext(
        IHost host,
        FakeRouteDiscovery routeDiscovery,
        FakeDnsProvider dnsProvider)
    {
        _host = host;
        RouteDiscovery = routeDiscovery;
        DnsProvider = dnsProvider;
        Metrics = host.Services.GetRequiredService<FennathMetrics>();
        DnsChannel = host.Services.GetRequiredService<DnsCommandChannel>();
        RouteAggregator = host.Services.GetRequiredService<RouteAggregator>();
    }

    public HttpClient CreateClient()
    {
        return _host.Services.GetRequiredService<IServer>() is TestServer testServer
            ? testServer.CreateClient()
            : throw new InvalidOperationException("Host is not using TestServer");
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
