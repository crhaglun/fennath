using Fennath.Discovery;

namespace Fennath.Tests.Helpers;

/// <summary>
/// Controllable route discovery source for integration tests.
/// Call <see cref="SetRoutes"/> to push routes and fire the RoutesChanged event.
/// </summary>
public sealed class FakeRouteDiscovery : IRouteDiscovery
{
    private List<DiscoveredRoute> _routes = [];

    public event Action? RoutesChanged;

    public IReadOnlyList<DiscoveredRoute> GetRoutes() => _routes;

    public void SetRoutes(params DiscoveredRoute[] routes)
    {
        _routes = [.. routes];
        RoutesChanged?.Invoke();
    }
}
