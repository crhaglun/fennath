using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Discovery;

/// <summary>
/// Discovers routes from the application configuration (appsettings.json / env vars).
/// Watches for configuration changes via IOptionsMonitor and emits RoutesChanged on reload.
/// </summary>
public sealed partial class StaticRouteDiscovery : IRouteDiscovery, IDisposable
{
    private readonly ILogger<StaticRouteDiscovery> _logger;
    private readonly IDisposable? _changeListener;
    private List<DiscoveredRoute> _routes = [];

    public event Action? RoutesChanged;

    public StaticRouteDiscovery(IOptionsMonitor<FennathConfig> optionsMonitor, ILogger<StaticRouteDiscovery> logger)
    {
        _logger = logger;

        LoadRoutes(optionsMonitor.CurrentValue);

        _changeListener = optionsMonitor.OnChange(config =>
        {
            LogConfigChanged(_logger);

            try
            {
                LoadRoutes(config);
                RoutesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                LogConfigReloadFailed(_logger, ex);
            }
        });
    }

    public IReadOnlyList<DiscoveredRoute> GetRoutes() => _routes;

    private void LoadRoutes(FennathConfig config)
    {
        _routes = config.Routes.Select(r => new DiscoveredRoute(
            Subdomain: r.Subdomain,
            BackendUrl: r.Backend,
            Source: "static",
            HealthCheckPath: r.HealthCheck?.Path,
            HealthCheckIntervalSeconds: r.HealthCheck?.IntervalSeconds
        )).ToList();

        LogRoutesLoaded(_logger, _routes.Count);
    }

    public void Dispose()
    {
        _changeListener?.Dispose();
    }

    [LoggerMessage(EventId = 1210, Level = LogLevel.Information, Message = "Configuration changed, reloading routes")]
    private static partial void LogConfigChanged(ILogger logger);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Error, Message = "Failed to reload configuration, keeping previous routes")]
    private static partial void LogConfigReloadFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1212, Level = LogLevel.Information, Message = "Loaded {count} routes from static config")]
    private static partial void LogRoutesLoaded(ILogger logger, int count);
}
