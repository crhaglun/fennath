using Fennath.Telemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Fennath.Sidecar.Telemetry;

/// <summary>
/// Configures OpenTelemetry traces, metrics, and logs for the sidecar.
/// OTLP export is always registered — the SDK reads standard OTEL_* environment
/// variables. When no endpoint is set, the exporter is a no-op.
/// </summary>
public static class SidecarTelemetrySetup
{
    public static IServiceCollection AddSidecarTelemetry(this IServiceCollection services)
    {
        services.AddSingleton<FennathMetrics>();

        var otel = services.AddOpenTelemetry();

        otel.WithTracing(tracing =>
        {
            tracing
                .AddSource(FennathMetrics.MeterName)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter();
        });

        otel.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(FennathMetrics.MeterName)
                .AddHttpClientInstrumentation()
                .AddOtlpExporter();
        });

        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otelLogging =>
            {
                otelLogging.IncludeFormattedMessage = true;
                otelLogging.IncludeScopes = true;
                otelLogging.AddOtlpExporter();
            });
        });

        return services;
    }
}
