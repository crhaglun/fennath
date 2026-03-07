using Fennath.Certificates;
using Fennath.Configuration;
using Fennath.Discovery;
using Fennath.Proxy;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services
    .AddOptions<FennathConfig>()
    .BindConfiguration(FennathConfig.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FennathConfig>, FennathConfigValidator>();

builder.Services.AddFennathTelemetry(builder.Configuration);
builder.Services.AddFennathProxy(builder.Configuration);
builder.Services.AddHealthChecks();

// Configure Kestrel TLS with dynamic certificate selection
builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    serverOptions.ConfigureHttpsDefaults(httpsOptions =>
    {
        CertificateStore? store = null;
        httpsOptions.ServerCertificateSelector = (connectionContext, hostname) =>
        {
            if (hostname is null) return null;
            store ??= serverOptions.ApplicationServices.GetRequiredService<CertificateStore>();
            return store.GetCertificate(hostname);
        };
    });
});

var app = builder.Build();

// Start Docker discovery (async init: snapshot running containers + subscribe to events)
var dockerDiscovery = app.Services.GetService<DockerRouteDiscovery>();
if (dockerDiscovery is not null)
{
    await dockerDiscovery.StartAsync(CancellationToken.None);
}

// Eagerly resolve RouteAggregator to trigger initial route loading
_ = app.Services.GetRequiredService<RouteAggregator>();

app.MapHealthChecks("/healthz");
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.UseMiddleware<Fennath.Telemetry.ProxyMetricsMiddleware>();
});

var config = app.Services.GetRequiredService<IOptions<FennathConfig>>().Value;

if (config.Certificates.Staging)
{
    Log.StagingModeWarning(app.Logger);
}

Log.Starting(app.Logger, config.Domain);

await app.RunAsync();

return 0;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Running with Let's Encrypt STAGING certificates (not browser-trusted)")]
    public static partial void StagingModeWarning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fennath starting for domain {domain}")]
    public static partial void Starting(ILogger logger, string domain);
}
