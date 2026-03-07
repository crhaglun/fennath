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

// Graceful shutdown — allow in-flight requests to drain before terminating
builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddFennathTelemetry();
builder.Services.AddFennathProxy(builder.Configuration);
builder.Services.AddHealthChecks();

// Configure Kestrel TLS with dynamic certificate selection
builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    var fennathConfig = context.Configuration
        .GetSection(FennathConfig.SectionName)
        .Get<FennathConfig>();

    var httpsPort = fennathConfig?.Server.HttpsPort ?? 443;
    var httpPort = fennathConfig?.Server.HttpPort ?? 80;

    serverOptions.ListenAnyIP(httpsPort, listenOptions =>
    {
        listenOptions.UseHttps(httpsOptions =>
        {
            CertificateStore? store = null;
            httpsOptions.ServerCertificateSelector = (_, _) =>
            {
                store ??= serverOptions.ApplicationServices.GetRequiredService<CertificateStore>();
                return store.GetCertificate();
            };
        });
    });

    serverOptions.ListenAnyIP(httpPort);
});

var app = builder.Build();

var config = app.Services.GetRequiredService<IOptions<FennathConfig>>().Value;

// HTTP → HTTPS redirect (when enabled in config)
if (config.Server.HttpToHttpsRedirect)
{
    app.UseHttpsRedirection();
}

// Eagerly resolve RouteAggregator to trigger initial route loading
_ = app.Services.GetRequiredService<RouteAggregator>();

app.MapHealthChecks("/healthz");
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.UseMiddleware<Fennath.Telemetry.ProxyMetricsMiddleware>();
});

if (config.Certificates.Staging)
{
    Log.StagingModeWarning(app.Logger);
}

Log.Starting(app.Logger, config.Domain);

// Ensure a valid certificate exists before accepting traffic.
// DNS-01 challenges don't need the web server, so we block here on first launch.
if (app.Services.GetRequiredService<CertificateStore>().GetExpiry() is null)
{
    Log.ProvisioningCertificate(app.Logger, config.Domain);
    await app.Services.GetRequiredService<AcmeService>().ProvisionWildcardCertificateAsync();
}

await app.RunAsync();

return 0;

internal static partial class Log
{
    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "Running with Let's Encrypt STAGING certificates (not browser-trusted)")]
    public static partial void StagingModeWarning(ILogger logger);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Fennath starting for domain {domain}")]
    public static partial void Starting(ILogger logger, string domain);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information, Message = "No certificate on disk for {domain} — provisioning from Let's Encrypt before accepting traffic")]
    public static partial void ProvisioningCertificate(ILogger logger, string domain);
}
