using System.Diagnostics.Metrics;

namespace Fennath.Telemetry;

/// <summary>
/// Central definition of all Fennath custom metrics instruments.
/// Uses System.Diagnostics.Metrics (the .NET native API consumed by OpenTelemetry).
/// Metric names follow ADR-006.
/// </summary>
public sealed class FennathMetrics
{
    public const string MeterName = "Fennath";

    private readonly Meter _meter;

    public FennathMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        DnsUpdatesTotal = _meter.CreateCounter<long>(
            "fennath.dns.update.total",
            description: "Number of DNS record update operations");

        IpChangesTotal = _meter.CreateCounter<long>(
            "fennath.ip.changes.total",
            description: "Number of public IP address changes detected");

        CertExpiryDays = _meter.CreateGauge<double>(
            "fennath.cert.expiry_days",
            unit: "d",
            description: "Days until certificate expiry per hostname");

        AcmeProvisioningTotal = _meter.CreateCounter<long>(
            "fennath.acme.provisioning.total",
            description: "Number of ACME certificate provisioning attempts by result");

        DnsRecordsCreated = _meter.CreateCounter<long>(
            "fennath.dns.records.created",
            description: "Number of DNS A records created");

        DnsRecordsRemoved = _meter.CreateCounter<long>(
            "fennath.dns.records.removed",
            description: "Number of DNS A records removed");

        RequestsTotal = _meter.CreateCounter<long>(
            "fennath.requests.total",
            description: "Total proxied requests by route and status code");

        RequestDuration = _meter.CreateHistogram<double>(
            "fennath.request.duration",
            unit: "ms",
            description: "Proxied request duration by route");
    }

    public Counter<long> DnsUpdatesTotal { get; }
    public Counter<long> IpChangesTotal { get; }
    public Gauge<double> CertExpiryDays { get; }
    public Counter<long> AcmeProvisioningTotal { get; }
    public Counter<long> DnsRecordsCreated { get; }
    public Counter<long> DnsRecordsRemoved { get; }
    public Counter<long> RequestsTotal { get; }
    public Histogram<double> RequestDuration { get; }
}
