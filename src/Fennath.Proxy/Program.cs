using Fennath.Certificates;
using Fennath.Configuration;
using Fennath.Proxy;
using Fennath.Proxy.Configuration;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// YARP route configuration — written by the operator container to the shared volume.
// .NET's built-in file watcher detects changes and YARP's LoadFromConfig() reloads automatically.
// We need to resolve the path before building the host, so we bind config eagerly here.
var yarpConfigPath = builder.Configuration.GetSection("Fennath")["YarpConfigPath"]
    ?? "/data/shared/yarp-config.json";
builder.Configuration.AddJsonFile(yarpConfigPath, optional: true, reloadOnChange: true);

builder.Services
    .AddOptions<ProxyConfig>()
    .BindConfiguration(ProxyConfig.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ProxyConfig>, ValidateProxyConfig>();

builder.Services
    .AddOptions<CertificateStoreOptions>()
    .Configure<IOptions<ProxyConfig>>((store, proxy) =>
    {
        store.Domain = proxy.Value.Domain;
        store.Subdomain = proxy.Value.Subdomain;
        store.StoragePath = proxy.Value.Certificates.StoragePath;
    });

builder.Services.AddOptions<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>()
    .Configure<IOptions<ProxyConfig>>((httpsOptions, proxyConfig) =>
    {
        httpsOptions.HttpsPort = proxyConfig.Value.Server.ExternalHttpsPort;
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
    var config = serverOptions.ApplicationServices.GetRequiredService<IOptions<ProxyConfig>>().Value;

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

var config = app.Services.GetRequiredService<IOptions<ProxyConfig>>().Value;

app.MapHealthChecks("/healthz");
app.UseHttpsRedirection();
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.UseMiddleware<Fennath.Telemetry.ProxyMetricsMiddleware>();
});

Log.Starting(app.Logger, config.EffectiveDomain);

await app.RunAsync();

return 0;

internal static partial class Log
{
    [LoggerMessage(EventId = 1301, Level = LogLevel.Information, Message = "Fennath proxy starting for domain {domain}")]
    public static partial void Starting(ILogger logger, string domain);
}
