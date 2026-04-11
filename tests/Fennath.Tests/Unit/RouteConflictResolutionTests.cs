using Fennath.Discovery;
using Fennath.Operator.Discovery;

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

        var merged = ProxyConfigWriter.Merge(routes);

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

        var merged = ProxyConfigWriter.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Empty_input_produces_empty_output()
    {
        var merged = ProxyConfigWriter.Merge([]);

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

        var merged = ProxyConfigWriter.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(1);
        await Assert.That(merged[0].BackendUrl).IsEqualTo("http://container1:8080");
    }

    [Test]
    public async Task Apex_route_is_preserved_through_merge()
    {
        var routes = new List<DiscoveredRoute>
        {
            new(DiscoveredRoute.ApexMarker, "http://localhost:8080", "docker"),
            new("grafana", "http://localhost:3000", "docker"),
        };

        var merged = ProxyConfigWriter.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(2);
        var apex = merged.First(r => r.IsApex);
        await Assert.That(apex.BackendUrl).IsEqualTo("http://localhost:8080");
    }

    [Test]
    public async Task Static_route_takes_precedence_over_docker_for_same_subdomain()
    {
        // Static routes are listed first (by DI registration order in Program.cs),
        // and Merge keeps the first occurrence per subdomain.
        var routes = new List<DiscoveredRoute>
        {
            new("myapp", "http://192.168.1.50:8080", "static"),
            new("myapp", "http://docker-container:9090", "docker:abc123"),
        };

        var merged = ProxyConfigWriter.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(1);
        await Assert.That(merged[0].BackendUrl).IsEqualTo("http://192.168.1.50:8080");
        await Assert.That(merged[0].Source).IsEqualTo("static");
    }

    [Test]
    public async Task Static_and_docker_routes_for_different_subdomains_coexist()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("nas", "http://192.168.1.50:5000", "static"),
            new("grafana", "http://grafana-container:3000", "docker:abc123"),
        };

        var merged = ProxyConfigWriter.Merge(routes);

        await Assert.That(merged).Count().IsEqualTo(2);
    }
}
