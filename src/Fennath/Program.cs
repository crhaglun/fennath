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
builder.Services.AddSingleton<IValidateOptions<FennathConfig>, FennathConfigValidator>();

// Graceful shutdown — allow in-flight requests to drain before terminating
builder.Services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.AddFennathTelemetry();
builder.Services.AddFennathProxy(builder.Configuration);
builder.Services.AddHealthChecks();

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

Log.Starting(app.Logger, config.Domain);

// Ensure a valid certificate exists before accepting traffic.
// DNS-01 challenges don't need the web server, so we block here on first launch.
if (app.Services.GetRequiredService<CertificateStore>().GetExpiry() is null)
{
    Log.ProvisioningCertificate(app.Logger, config.Domain);
    try
    {
        await app.Services.GetRequiredService<AcmeService>().ProvisionWildcardCertificateAsync();
    }
    catch (Exception ex)
    {
        Log.ProvisioningFailed(app.Logger, ex);
        return 1;
    }
}

await app.RunAsync();

return 0;

internal static partial class Log
{
    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Fennath starting for domain {domain}")]
    public static partial void Starting(ILogger logger, string domain);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information, Message = "No certificate on disk for {domain} — provisioning from Let's Encrypt before accepting traffic")]
    public static partial void ProvisioningCertificate(ILogger logger, string domain);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Critical, Message = "Certificate provisioning failed — check DNS provider credentials, ACME server availability, and network connectivity")]
    public static partial void ProvisioningFailed(ILogger logger, Exception ex);
}
