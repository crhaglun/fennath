using System.Diagnostics;
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

    public static readonly ActivitySource ActivitySource = new(MeterName);

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

        BackendHealth = _meter.CreateGauge<int>(
            "fennath.backend.health",
            description: "Backend health status per route (1=up, 0=down)");
    }

    /// <summary>Counter for DNS record update operations.</summary>
    public Counter<long> DnsUpdatesTotal { get; }

    /// <summary>Counter for detected public IP changes.</summary>
    public Counter<long> IpChangesTotal { get; }

    /// <summary>Gauge reporting days until certificate expiry, per hostname.</summary>
    public Gauge<double> CertExpiryDays { get; }

    /// <summary>Gauge reporting backend health per route (1=up, 0=down).</summary>
    public Gauge<int> BackendHealth { get; }
}
