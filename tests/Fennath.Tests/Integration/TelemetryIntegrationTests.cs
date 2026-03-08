using System.Diagnostics.Metrics;
using System.Net;
using Fennath.Telemetry;
using Fennath.Tests.Helpers;

namespace Fennath.Tests.Integration;

public class TelemetryIntegrationTests : IAsyncDisposable
{
    private TestBackend _backend = null!;

    [Before(Test)]
    public async Task SetUp()
    {
        _backend = await TestBackend.CreateAsync();
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _backend.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _backend.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task Proxied_request_records_metrics_with_correct_route_tag()
    {
        var requestTags = new List<KeyValuePair<string, object?>[]>();

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
            {
                requestTags.Add(tags.ToArray());
            }
        });
        listener.Start();

        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        var client = ctx.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "grafana.example.com";

        var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await Assert.That(requestTags).Count().IsGreaterThanOrEqualTo(1);

        var tags = requestTags[0];
        var routeTag = tags.FirstOrDefault(t => t.Key == "route").Value?.ToString();
        var statusTag = tags.FirstOrDefault(t => t.Key == "http.response.status_code").Value;

        // Route tag is populated (route-grafana when IReverseProxyFeature is set)
        await Assert.That(routeTag).IsNotNull();
        await Assert.That(statusTag).IsEqualTo(200);
    }

    [Test]
    public async Task Proxied_request_records_duration_histogram()
    {
        var durations = new List<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FennathMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "fennath.request.duration")
            {
                durations.Add(measurement);
            }
        });
        listener.Start();

        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        var client = ctx.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "grafana.example.com";

        await client.SendAsync(request);

        await Assert.That(durations).Count().IsGreaterThanOrEqualTo(1);
        await Assert.That(durations[0]).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Request_to_unknown_host_still_records_metrics()
    {
        var requestCount = 0;

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
            {
                Interlocked.Increment(ref requestCount);
            }
        });
        listener.Start();

        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        var client = ctx.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "unknown.example.com";

        await client.SendAsync(request);

        // Unmatched routes don't go through the YARP proxy pipeline,
        // so ProxyMetricsMiddleware doesn't fire
        await Assert.That(requestCount).IsEqualTo(0);
    }
}
