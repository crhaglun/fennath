using Fennath.Discovery;

namespace Fennath.Tests.Unit;

public class RouteConflictResolutionTests
{
    [Test]
    public async Task Routes_from_different_subdomains_are_all_kept()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://localhost:3000", "docker"),
            new("git", "http://localhost:3001", "docker"),
            new("api", "http://localhost:8080", "docker"),
        };

        var merged = RouteAggregator.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Subdomain_matching_is_case_insensitive()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("MyApp", "http://container1:8080", "docker"),
            new("myapp", "http://container2:9090", "docker"),
        };

        var merged = RouteAggregator.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Empty_input_produces_empty_output()
    {
        var merged = RouteAggregator.Merge([]);

        await Assert.That(merged).IsEmpty();
    }

    [Test]
    public async Task Multiple_docker_routes_for_same_subdomain_keeps_first()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("myapp", "http://container1:8080", "docker"),
            new("myapp", "http://container2:9090", "docker"),
        };

        var merged = RouteAggregator.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(1);
        await Assert.That(merged[0].BackendUrl).IsEqualTo("http://container1:8080");
    }

    [Test]
    public async Task Health_check_metadata_is_preserved_through_merge()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://localhost:3000", "docker", "/api/health", 60),
        };

        var merged = RouteAggregator.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(1);
        await Assert.That(merged[0].HealthCheckPath).IsEqualTo("/api/health");
        await Assert.That(merged[0].HealthCheckIntervalSeconds).IsEqualTo(60);
    }

    [Test]
    public async Task Apex_route_is_preserved_through_merge()
    {
        var routes = new List<DiscoveredRoute>
        {
            new(DiscoveredRoute.ApexMarker, "http://localhost:8080", "docker"),
            new("grafana", "http://localhost:3000", "docker"),
        };

        var merged = RouteAggregator.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(2);
        var apex = merged.First(r => r.IsApex);
        await Assert.That(apex.BackendUrl).IsEqualTo("http://localhost:8080");
    }
}
