using Yarp.ReverseProxy.Configuration;

namespace Fennath.Discovery;

/// <summary>
/// Merges routes from all IRouteDiscovery sources and pushes updates
/// to YARP's InMemoryConfigProvider.
/// Static routes take precedence over other sources for the same subdomain.
/// </summary>
public sealed partial class RouteAggregator : IDisposable
{
    private readonly IReadOnlyList<IRouteDiscovery> _sources;
    private readonly InMemoryConfigProvider _yarpConfigProvider;
    private readonly string _domain;
    private readonly ILogger<RouteAggregator> _logger;
    private readonly Lock _lock = new();

    public RouteAggregator(
        IEnumerable<IRouteDiscovery> sources,
        InMemoryConfigProvider yarpConfigProvider,
        string domain,
        ILogger<RouteAggregator> logger)
    {
        _sources = sources.ToList();
        _yarpConfigProvider = yarpConfigProvider;
        _domain = domain;
        _logger = logger;

        foreach (var source in _sources)
        {
            source.RoutesChanged += OnSourceChanged;
        }

        RebuildRoutes();
    }

    private void OnSourceChanged()
    {
        lock (_lock)
        {
            RebuildRoutes();
        }
    }

    private void RebuildRoutes()
    {
        var merged = Merge(_sources.SelectMany(s => s.GetRoutes()).ToList());

        var yarpRoutes = new List<RouteConfig>();
        var yarpClusters = new List<ClusterConfig>();

        foreach (var route in merged)
        {
            var clusterId = $"cluster-{route.Subdomain}";
            var routeId = $"route-{route.Subdomain}";
            var host = $"{route.Subdomain}.{_domain}";

            yarpRoutes.Add(new RouteConfig
            {
                RouteId = routeId,
                ClusterId = clusterId,
                Match = new RouteMatch { Hosts = [host] }
            });

            var clusterBuilder = new ClusterConfig
            {
                ClusterId = clusterId,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["default"] = new DestinationConfig { Address = route.BackendUrl }
                },
                HealthCheck = route.HealthCheckPath is not null
                    ? new HealthCheckConfig
                    {
                        Active = new ActiveHealthCheckConfig
                        {
                            Enabled = true,
                            Path = route.HealthCheckPath,
                            Interval = TimeSpan.FromSeconds(route.HealthCheckIntervalSeconds ?? 30)
                        }
                    }
                    : null
            };

            yarpClusters.Add(clusterBuilder);
        }

        _yarpConfigProvider.Update(yarpRoutes, yarpClusters);
        LogRoutesUpdated(_logger, merged.Count);
    }

    /// <summary>
    /// Merges routes from all sources. Static routes win over non-static for the same subdomain.
    /// </summary>
    internal static List<DiscoveredRoute> Merge(List<DiscoveredRoute> allRoutes)
    {
        return allRoutes
            .GroupBy(r => r.Subdomain, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.Source == "static").First())
            .ToList();
    }

    public void Dispose()
    {
        foreach (var source in _sources)
        {
            source.RoutesChanged -= OnSourceChanged;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "YARP configuration updated with {count} routes")]
    private static partial void LogRoutesUpdated(ILogger logger, int count);
}
