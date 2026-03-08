using System.Diagnostics.Metrics;
using System.Net;
using Fennath.Telemetry;
using Fennath.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Fennath.Tests.Integration;

public class TelemetryTests : IAsyncDisposable
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
            {
                requestsRecorded.Add(measurement);
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "fennath.request.duration")
            {
                durationsRecorded.Add(measurement);
            }
        });
        listener.Start();

        using var fennath = await FennathTestHost.CreateWithMetricsAsync(
            ("grafana", _backend.Url));

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
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "fennath.dns.update.total")
            {
                dnsUpdates.Add(measurement);
            }

            if (instrument.Name == "fennath.ip.changes.total")
            {
                ipChanges.Add(measurement);
            }
        });
        listener.Start();

        using var fennath = await FennathTestHost.CreateWithMetricsAsync(
            ("grafana", _backend.Url));

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
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "fennath.cert.expiry_days")
            {
                expiryValues.Add(measurement);
            }
        });
        listener.Start();

        using var fennath = await FennathTestHost.CreateWithMetricsAsync(
            ("grafana", _backend.Url));

        var metrics = fennath.Services.GetRequiredService<FennathMetrics>();
        metrics.CertExpiryDays.Record(42.5,
            new KeyValuePair<string, object?>("hostname", "*.example.com"));

        await Assert.That(expiryValues).Count().IsEqualTo(1);
        await Assert.That(expiryValues[0]).IsEqualTo(42.5);
    }
}
