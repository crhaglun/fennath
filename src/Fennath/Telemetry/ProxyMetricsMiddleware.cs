using System.Diagnostics;
using System.Diagnostics.Metrics;
using Yarp.ReverseProxy.Model;

namespace Fennath.Telemetry;

/// <summary>
/// YARP pipeline middleware that records per-route request metrics.
/// Runs inside the proxy pipeline where IReverseProxyFeature is available.
/// </summary>
public sealed class ProxyMetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly Counter<long> _requestsTotal;
    private readonly Histogram<double> _requestDuration;

    public ProxyMetricsMiddleware(RequestDelegate next, IMeterFactory meterFactory)
    {
        _next = next;

        var meter = meterFactory.Create(FennathMetrics.MeterName);
        _requestsTotal = meter.CreateCounter<long>(
            "fennath.requests.total",
            description: "Total proxied requests by route and status code");
        _requestDuration = meter.CreateHistogram<double>(
            "fennath.request.duration",
            unit: "ms",
            description: "Proxied request duration by route");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        await _next(context);

        stopwatch.Stop();

        var route = context.GetReverseProxyFeature()?.Route?.Config?.RouteId ?? "unknown";
        var statusCode = context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var tags = new TagList
        {
            { "route", route },
            { "http.response.status_code", statusCode }
        };

        _requestsTotal.Add(1, tags);
        _requestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }
}
