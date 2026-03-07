namespace Fennath.Discovery;

/// <summary>
/// A discovered route from any source (static config, Docker labels, etc.).
/// </summary>
public sealed record DiscoveredRoute(
    string Subdomain,
    string BackendUrl,
    string Source,
    string? HealthCheckPath = null,
    int? HealthCheckIntervalSeconds = null);

/// <summary>
/// Abstraction for route discovery mechanisms.
/// Implementations watch their source and invoke OnRoutesChanged when routes are updated.
/// </summary>
public interface IRouteDiscovery
{
    /// <summary>
    /// Gets the current set of discovered routes.
    /// </summary>
    IReadOnlyList<DiscoveredRoute> GetRoutes();

    /// <summary>
    /// Fired when the set of routes from this source has changed.
    /// </summary>
    event Action? RoutesChanged;
}
