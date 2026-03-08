using System.Diagnostics;

namespace Fennath.Telemetry;

/// <summary>
/// YARP pipeline middleware that records per-route request metrics.
/// Runs inside the proxy pipeline where IReverseProxyFeature is available.
/// </summary>
public sealed class ProxyMetricsMiddleware(RequestDelegate Next, FennathMetrics Metrics)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        await Next(context);

        stopwatch.Stop();

        var route = context.GetReverseProxyFeature()?.Route?.Config?.RouteId ?? "unknown";

        var tags = new TagList
        {
            { "route", route },
            { "http.response.status_code", context.Response.StatusCode }
        };

        Metrics.RequestsTotal.Add(1, tags);
        Metrics.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }
}
