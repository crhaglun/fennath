using Fennath.Discovery;
using Fennath.Operator.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Operator.Discovery;

/// <summary>
/// Discovers routes from static configuration (non-Docker backends such as VMs,
/// physical servers, or services on other hosts). Routes are defined in the
/// <c>Fennath:StaticRoutes</c> config section and hot-reloaded via
/// <see cref="IOptionsMonitor{T}"/>.
/// </summary>
public sealed partial class StaticRouteDiscovery : IRouteDiscovery, IDisposable
{
    private readonly ILogger<StaticRouteDiscovery> _logger;
    private readonly IDisposable? _changeToken;
    private IReadOnlyList<DiscoveredRoute> _routes;

    // Classic constructor needed for OnChange callback that captures 'this'.
    public StaticRouteDiscovery(
        IOptionsMonitor<OperatorConfig> optionsMonitor,
        ILogger<StaticRouteDiscovery> logger)
    {
        _logger = logger;
        _routes = BuildRoutes(optionsMonitor.CurrentValue.StaticRoutes, logger);
        _changeToken = optionsMonitor.OnChange((config, _) =>
        {
            var newRoutes = BuildRoutes(config.StaticRoutes, _logger);
            var oldRoutes = Volatile.Read(ref _routes);

            if (!RoutesEqual(oldRoutes, newRoutes))
            {
                LogConfigChanged(_logger);
                Volatile.Write(ref _routes, newRoutes);
                RoutesChanged?.Invoke();
            }
        });
    }

    public IReadOnlyList<DiscoveredRoute> GetRoutes() => Volatile.Read(ref _routes);

    public event Action? RoutesChanged;

    public void Dispose() => _changeToken?.Dispose();

    internal static IReadOnlyList<DiscoveredRoute> BuildRoutes(
        List<StaticRouteEntry> entries, ILogger logger)
    {
        var routes = new List<DiscoveredRoute>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var subdomain = entry.Subdomain?.Trim() ?? "";
            var backendUrl = entry.BackendUrl?.Trim() ?? "";

            if (string.IsNullOrEmpty(subdomain))
            {
                LogInvalidEntry(logger, "(empty)", backendUrl, "subdomain is required");
                continue;
            }

            if (!Uri.TryCreate(backendUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                LogInvalidEntry(logger, subdomain, backendUrl, "backend URL must be an absolute http or https URI");
                continue;
            }

            if (!seen.Add(subdomain))
            {
                LogInvalidEntry(logger, subdomain, backendUrl, "duplicate subdomain (keeping first)");
                continue;
            }

            routes.Add(new DiscoveredRoute(subdomain, backendUrl, "static"));
        }

        LogRoutesLoaded(logger, routes.Count);
        return routes;
    }

    internal static bool RoutesEqual(
        IReadOnlyList<DiscoveredRoute> a, IReadOnlyList<DiscoveredRoute> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Information, Message = "Static route discovery loaded {count} routes")]
    private static partial void LogRoutesLoaded(ILogger logger, int count);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Skipping invalid static route: {subdomain} → {backendUrl} ({reason})")]
    private static partial void LogInvalidEntry(ILogger logger, string subdomain, string backendUrl, string reason);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information, Message = "Static routes config changed, reloading")]
    private static partial void LogConfigChanged(ILogger logger);
}

