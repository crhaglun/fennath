using Fennath.Configuration;
using Fennath.Discovery;
using Fennath.Proxy;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services
    .AddOptions<FennathConfig>()
    .BindConfiguration(FennathConfig.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FennathConfig>, FennathConfigValidator>();

builder.Services.AddFennathProxy();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Eagerly resolve RouteAggregator to trigger initial route loading
_ = app.Services.GetRequiredService<RouteAggregator>();

app.MapHealthChecks("/healthz");
app.MapReverseProxy();

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
