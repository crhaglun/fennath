using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Fennath.Configuration;
using Fennath.Telemetry;
using Yarp.ReverseProxy.Configuration;

namespace Fennath.Tests.Integration;

public class TelemetryTests : IAsyncDisposable
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
    public async Task Proxied_request_records_request_counter_and_duration()
    {
        var requestsRecorded = new List<long>();
        var durationsRecorded = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FennathMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "fennath.requests.total")
                requestsRecorded.Add(measurement);
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "fennath.request.duration")
                durationsRecorded.Add(measurement);
        });
        listener.Start();

        using var fennath = await CreateFennathTestHostWithMetrics(
            new RouteEntry { Subdomain = "grafana", Backend = _backendUrl });

        var client = fennath.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "grafana.example.com";

        var response = await client.SendAsync(request);

        listener.RecordObservableInstruments();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(requestsRecorded).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(durationsRecorded).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(durationsRecorded[0]).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task FennathMetrics_dns_counters_are_recordable()
    {
        var dnsUpdates = new List<long>();
        var ipChanges = new List<long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FennathMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "fennath.dns.update.total")
                dnsUpdates.Add(measurement);
            if (instrument.Name == "fennath.ip.changes.total")
                ipChanges.Add(measurement);
        });
        listener.Start();

        using var fennath = await CreateFennathTestHostWithMetrics(
            new RouteEntry { Subdomain = "grafana", Backend = _backendUrl });

        var metrics = fennath.Services.GetRequiredService<FennathMetrics>();
        metrics.DnsUpdatesTotal.Add(1);
        metrics.IpChangesTotal.Add(1);

        await Assert.That(dnsUpdates).Count().IsEqualTo(1);
        await Assert.That(ipChanges).Count().IsEqualTo(1);
    }

    [Test]
    public async Task FennathMetrics_cert_expiry_gauge_is_recordable()
    {
        var expiryValues = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FennathMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "fennath.cert.expiry_days")
                expiryValues.Add(measurement);
        });
        listener.Start();

        using var fennath = await CreateFennathTestHostWithMetrics(
            new RouteEntry { Subdomain = "grafana", Backend = _backendUrl });

        var metrics = fennath.Services.GetRequiredService<FennathMetrics>();
        metrics.CertExpiryDays.Record(42.5,
            new KeyValuePair<string, object?>("hostname", "*.example.com"));

        await Assert.That(expiryValues).Count().IsEqualTo(1);
        await Assert.That(expiryValues[0]).IsEqualTo(42.5);
    }

    private static async Task<IHost> CreateFennathTestHostWithMetrics(params RouteEntry[] routes)
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
                        endpoints.MapReverseProxy(proxyPipeline =>
                        {
                            proxyPipeline.UseMiddleware<ProxyMetricsMiddleware>();
                        });
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

file static class TelemetryTestHostExtensions
{
    public static HttpClient GetTestClient(this IHost host)
    {
        return host.Services.GetRequiredService<IServer>() is TestServer testServer
            ? testServer.CreateClient()
            : throw new InvalidOperationException("Host is not using TestServer");
    }
}
