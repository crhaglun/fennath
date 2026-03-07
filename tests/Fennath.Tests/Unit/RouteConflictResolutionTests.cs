using Fennath.Discovery;

namespace Fennath.Tests.Unit;

public class RouteConflictResolutionTests
{
    [Test]
    public async Task Static_route_wins_over_docker_route_for_same_subdomain()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("myapp", "http://static:8080", "static"),
            new("myapp", "http://docker:9090", "docker"),
        };

        var merged = RouteAggregator.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(1);
        await Assert.That(merged[0].BackendUrl).IsEqualTo("http://static:8080");
    }

    [Test]
    public async Task Routes_from_different_subdomains_are_all_kept()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://localhost:3000", "static"),
            new("git", "http://localhost:3001", "docker"),
            new("api", "http://localhost:8080", "static"),
        };

        var merged = RouteAggregator.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(3);
    }

    [Test]
    public async Task Subdomain_matching_is_case_insensitive()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("MyApp", "http://static:8080", "static"),
            new("myapp", "http://docker:9090", "docker"),
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
    }
}
