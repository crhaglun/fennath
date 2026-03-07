using System.Diagnostics;
using Yarp.ReverseProxy.Model;

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
        var statusCode = context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var tags = new TagList
        {
            { "route", route },
            { "http.response.status_code", statusCode }
        };

        Metrics.RequestsTotal.Add(1, tags);
        Metrics.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }
}
