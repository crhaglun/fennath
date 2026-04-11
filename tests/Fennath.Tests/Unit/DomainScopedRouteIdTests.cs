using Fennath.Operator.Discovery;
using Fennath.Discovery;

namespace Fennath.Tests.Unit;

/// <summary>
/// Tests for domain-scoped route and cluster IDs in <see cref="ProxyConfigWriter.BuildYarpConfig"/>.
/// Multi-operator deployments need unique IDs across operators to prevent collision.
/// </summary>
public class DomainScopedRouteIdTests
{
    [Test]
    public async Task Route_ids_include_domain_slug()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://grafana:3000", "docker")
        };

        var (yarpRoutes, _) = ProxyConfigWriter.BuildYarpConfig(routes, "lab.example.com");

        await Assert.That(yarpRoutes).ContainsKey("route-lab-example-com-grafana");
    }

    [Test]
    public async Task Cluster_ids_include_domain_slug()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://grafana:3000", "docker")
        };

        var (_, clusters) = ProxyConfigWriter.BuildYarpConfig(routes, "lab.example.com");

        await Assert.That(clusters).ContainsKey("cluster-lab-example-com-grafana");
    }

    [Test]
    public async Task Route_match_host_uses_full_domain()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://grafana:3000", "docker")
        };

        var (yarpRoutes, _) = ProxyConfigWriter.BuildYarpConfig(routes, "lab.example.com");
        var host = yarpRoutes["route-lab-example-com-grafana"].Match.Hosts![0];

        await Assert.That(host).IsEqualTo("grafana.lab.example.com");
    }

    [Test]
    public async Task Apex_route_uses_bare_domain_as_host()
    {
        var routes = new List<DiscoveredRoute>
        {
            new(DiscoveredRoute.ApexMarker, "http://myapp:8080", "docker")
        };

        var (yarpRoutes, _) = ProxyConfigWriter.BuildYarpConfig(routes, "lab.example.com");
        var host = yarpRoutes["route-lab-example-com-@"].Match.Hosts![0];

        await Assert.That(host).IsEqualTo("lab.example.com");
    }

    [Test]
    public async Task Different_domains_produce_different_ids()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://grafana:3000", "docker")
        };

        var (routesA, _) = ProxyConfigWriter.BuildYarpConfig(routes, "lab.example.com");
        var (routesB, _) = ProxyConfigWriter.BuildYarpConfig(routes, "apps.example.org");

        await Assert.That(routesA.Keys.First()).IsNotEqualTo(routesB.Keys.First());
    }

    [Test]
    public async Task Multiple_routes_all_get_domain_scoped_ids()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://grafana:3000", "docker"),
            new("api", "http://api:8080", "docker"),
        };

        var (yarpRoutes, clusters) = ProxyConfigWriter.BuildYarpConfig(routes, "lab.example.com");

        await Assert.That(yarpRoutes).ContainsKey("route-lab-example-com-grafana");
        await Assert.That(yarpRoutes).ContainsKey("route-lab-example-com-api");
        await Assert.That(clusters).ContainsKey("cluster-lab-example-com-grafana");
        await Assert.That(clusters).ContainsKey("cluster-lab-example-com-api");
    }

    [Test]
    public async Task Cluster_destination_has_correct_backend_url()
    {
        var routes = new List<DiscoveredRoute>
        {
            new("grafana", "http://grafana:3000", "docker")
        };

        var (_, clusters) = ProxyConfigWriter.BuildYarpConfig(routes, "lab.example.com");
        var address = clusters["cluster-lab-example-com-grafana"].Destinations!["default"].Address;

        await Assert.That(address).IsEqualTo("http://grafana:3000");
    }
}
