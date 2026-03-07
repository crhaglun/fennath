using Fennath.Configuration;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Fennath.Telemetry;

/// <summary>
/// Configures OpenTelemetry traces, metrics, and logs for Fennath.
/// When no OTLP endpoint is configured, telemetry is still collected
/// in-process (useful for tests) but not exported.
/// </summary>
public static class TelemetrySetup
{
    public static IServiceCollection AddFennathTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<FennathMetrics>();

        var telemetryConfig = configuration
            .GetSection($"{FennathConfig.SectionName}:Telemetry")
            .Get<TelemetryConfig>() ?? new TelemetryConfig();

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: telemetryConfig.ServiceName,
                serviceVersion: typeof(TelemetrySetup).Assembly.GetName().Version?.ToString() ?? "0.0.0");

        var otel = services.AddOpenTelemetry();

        // Traces
        otel.WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(resourceBuilder)
                .AddSource(FennathMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (telemetryConfig.Endpoint is not null)
            {
                tracing.AddOtlpExporter(opts => ConfigureOtlp(opts, telemetryConfig));
            }
        });

        // Metrics
        otel.WithMetrics(metrics =>
        {
            metrics
                .SetResourceBuilder(resourceBuilder)
                .AddMeter(FennathMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (telemetryConfig.Endpoint is not null)
            {
                metrics.AddOtlpExporter(opts => ConfigureOtlp(opts, telemetryConfig));
            }
        });

        // Logs
        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otelLogging =>
            {
                otelLogging.SetResourceBuilder(resourceBuilder);
                otelLogging.IncludeFormattedMessage = true;
                otelLogging.IncludeScopes = true;

                if (telemetryConfig.Endpoint is not null)
                {
                    otelLogging.AddOtlpExporter(opts => ConfigureOtlp(opts, telemetryConfig));
                }
            });
        });

        return services;
    }

    private static void ConfigureOtlp(
        OpenTelemetry.Exporter.OtlpExporterOptions opts,
        TelemetryConfig config)
    {
        opts.Endpoint = new Uri(config.Endpoint!);
        opts.Protocol = config.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf
            : OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;

        if (config.Headers.Count > 0)
        {
            opts.Headers = string.Join(",",
                config.Headers.Select(h => $"{h.Key}={h.Value}"));
        }
    }
}
