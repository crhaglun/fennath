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
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FennathConfig>, ProxyConfigValidator>();

builder.Services.AddOptions<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>()
    .Configure<IOptions<FennathConfig>>((httpsOptions, fennathConfig) =>
    {
        httpsOptions.HttpsPort = fennathConfig.Value.Server.ExternalHttpsPort;
    });

// Graceful shutdown — allow in-flight requests to drain before terminating
builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddFennathTelemetry();
builder.Services.AddFennathProxy(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<Fennath.Proxy.CertificateHealthCheck>("certificate");

// Configure Kestrel TLS with dynamic certificate selection
builder.WebHost.ConfigureKestrel((_, serverOptions) =>
{
    var config = serverOptions.ApplicationServices.GetRequiredService<IOptions<FennathConfig>>().Value;

    serverOptions.ListenAnyIP(config.Server.HttpsPort, listenOptions =>
    {
        listenOptions.UseHttps(httpsOptions =>
        {
            var store = serverOptions.ApplicationServices.GetRequiredService<CertificateStore>();
            httpsOptions.ServerCertificateSelector = (_, _) => store.GetCertificate();
        });
    });

    serverOptions.ListenAnyIP(config.Server.HttpPort);
});

var app = builder.Build();

var config = app.Services.GetRequiredService<IOptions<FennathConfig>>().Value;

// HTTP → HTTPS redirect (when enabled in config)
app.UseHttpsRedirection();

// Eagerly resolve RouteAggregator to trigger initial route loading
_ = app.Services.GetRequiredService<RouteAggregator>();

app.MapHealthChecks("/healthz");
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.UseMiddleware<Fennath.Telemetry.ProxyMetricsMiddleware>();
});

Log.Starting(app.Logger, config.EffectiveDomain);

await app.StartAsync();

// Wait for a valid certificate to appear on the shared volume.
// The sidecar container provisions and writes certs; we watch for changes.
// The host is already running (OTel active, /healthz available on HTTP)
// but TLS handshakes will fail until a cert is available.
if (app.Services.GetRequiredService<CertificateStore>().GetExpiry() is null)
{
    Log.WaitingForCertificate(app.Logger, config.EffectiveDomain);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(app.Lifetime.ApplicationStopping);
    cts.CancelAfter(TimeSpan.FromMinutes(10));

    try
    {
        var checks = 0;
        while (app.Services.GetRequiredService<CertificateStore>().GetExpiry() is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
            if (++checks % 12 == 0)
            {
                Log.StillWaitingForCertificate(app.Logger, checks / 12);
            }
        }

        Log.CertificateAvailable(app.Logger, config.EffectiveDomain);
    }
    catch (OperationCanceledException)
    {
        Log.CertificateWaitTimedOut(app.Logger);
        await app.StopAsync();
        return 1;
    }
}

await app.WaitForShutdownAsync();

return 0;

internal static partial class Log
{
    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Fennath proxy starting for domain {domain}")]
    public static partial void Starting(ILogger logger, string domain);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information, Message = "No certificate on disk for {domain} — waiting for sidecar to provision")]
    public static partial void WaitingForCertificate(ILogger logger, string domain);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Warning, Message = "Still waiting for sidecar certificate — {minutes} minute(s) elapsed")]
    public static partial void StillWaitingForCertificate(ILogger logger, int minutes);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Information, Message = "Certificate available for {domain} — accepting HTTPS traffic")]
    public static partial void CertificateAvailable(ILogger logger, string domain);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Critical, Message = "Timed out waiting for certificate from sidecar — check that fennath-sidecar is running and DNS credentials are correct")]
    public static partial void CertificateWaitTimedOut(ILogger logger);
}
