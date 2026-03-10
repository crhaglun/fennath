using Fennath.Operator.Dns;
using Fennath.Telemetry;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Tests.Helpers;

/// <summary>
/// Wraps a test Fennath host, exposing test doubles for assertions.
/// </summary>
public sealed class FennathTestContext : IAsyncDisposable
{
    private readonly IHost _host;

    public InMemoryConfigProvider YarpConfig { get; }
    public FakeDnsProvider DnsProvider { get; }
    public FennathMetrics Metrics { get; }
    public DnsCommandChannel DnsChannel { get; }

    internal FennathTestContext(
        IHost host,
        InMemoryConfigProvider yarpConfig,
        FakeDnsProvider dnsProvider)
    {
        _host = host;
        YarpConfig = yarpConfig;
        DnsProvider = dnsProvider;
        Metrics = host.Services.GetRequiredService<FennathMetrics>();
        DnsChannel = host.Services.GetRequiredService<DnsCommandChannel>();
    }

    /// <summary>
    /// Updates the YARP routing configuration, simulating what happens when
    /// the operator writes a new yarp-config.json.
    /// </summary>
    public void UpdateRoutes(params (string Subdomain, string Backend)[] routes)
    {
        var (yarpRoutes, yarpClusters) = FennathTestHost.BuildYarpConfig("example.com", routes);
        YarpConfig.Update(yarpRoutes, yarpClusters);
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
