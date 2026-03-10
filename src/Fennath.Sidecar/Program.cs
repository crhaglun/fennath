using Fennath.Certificates;
using Fennath.Configuration;
using Fennath.Discovery;
using Fennath.Sidecar;
using Fennath.Sidecar.Certificates;
using Fennath.Sidecar.Discovery;
using Fennath.Sidecar.Dns;
using Fennath.Sidecar.Telemetry;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services
    .AddOptions<FennathConfig>()
    .BindConfiguration(FennathConfig.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FennathConfig>, SidecarConfigValidator>();

// Telemetry
builder.Services.AddSidecarTelemetry();

// Docker route discovery — polls Docker API for labeled containers
builder.Services.AddSingleton<DockerRouteDiscovery>();
builder.Services.AddSingleton<IRouteDiscovery>(sp => sp.GetRequiredService<DockerRouteDiscovery>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DockerRouteDiscovery>());

// Proxy config writer — writes YARP config JSON to shared volume when routes change
builder.Services.AddHostedService<ProxyConfigWriter>();

// DNS
builder.Services.AddHttpClient<LoopiaDnsProvider>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<IDnsProvider>(sp => sp.GetRequiredService<LoopiaDnsProvider>());
builder.Services.AddHttpClient<PublicIpResolver>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<PublicIpResolver>();
builder.Services.AddSingleton<DnsCommandChannel>();
builder.Services.AddHostedService<IpMonitorService>();
builder.Services.AddHostedService<DnsReconciliationService>();

// Certificates
builder.Services.AddSingleton<CertificateStore>();
builder.Services.AddSingleton<DnsPropagationChecker>();
builder.Services.AddSingleton<AcmeService>();
builder.Services.AddHostedService<CertificateRenewalService>();

var host = builder.Build();

var config = host.Services.GetRequiredService<IOptions<FennathConfig>>().Value;
var logger = host.Services.GetRequiredService<ILogger<Program>>();

Log.Starting(logger, config.EffectiveDomain);

await host.RunAsync();

return 0;

internal static partial class Log
{
    [LoggerMessage(EventId = 1600, Level = LogLevel.Information, Message = "Fennath sidecar starting for domain {domain}")]
    public static partial void Starting(ILogger logger, string domain);
}
