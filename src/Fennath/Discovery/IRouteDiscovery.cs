namespace Fennath.Discovery;

/// <summary>
/// A discovered route from any source (static config, Docker labels, etc.).
/// Use <see cref="ApexMarker"/> as the subdomain to route the bare/apex domain.
/// </summary>
public sealed record DiscoveredRoute(
    string Subdomain,
    string BackendUrl,
    string Source,
    string? HealthCheckPath = null,
    int? HealthCheckIntervalSeconds = null)
{
    /// <summary>
    /// Conventional marker for the apex/root domain (e.g., "labs.example.com" with no subdomain prefix).
    /// Follows DNS convention where @ represents the zone apex.
    /// </summary>
    public const string ApexMarker = "@";

    /// <summary>
    /// Returns true if this route targets the apex/root domain.
    /// </summary>
    public bool IsApex => string.Equals(Subdomain, ApexMarker, StringComparison.Ordinal);
}

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
