using Fennath.Discovery;

namespace Fennath.Tests.Unit;

public class DockerLabelParsingTests
{
    [Test]
    public async Task Parses_single_subdomain_with_backend()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            ["fennath.backend"] = "http://localhost:3000"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", labels);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].Subdomain).IsEqualTo("grafana");
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://localhost:3000");
        await Assert.That(routes[0].Source).IsEqualTo("docker:abc123");
    }

    [Test]
    public async Task Parses_comma_separated_subdomains()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "@, www",
            ["fennath.backend"] = "http://localhost:8080"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("def456", labels);

        await Assert.That(routes).Count().IsEqualTo(2);
        await Assert.That(routes[0].Subdomain).IsEqualTo("@");
        await Assert.That(routes[0].IsApex).IsTrue();
        await Assert.That(routes[1].Subdomain).IsEqualTo("www");
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://localhost:8080");
        await Assert.That(routes[1].BackendUrl).IsEqualTo("http://localhost:8080");
    }

    [Test]
    public async Task Missing_backend_label_returns_empty()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", labels);

        await Assert.That(routes).IsEmpty();
    }

    [Test]
    public async Task Missing_subdomain_label_returns_empty()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.backend"] = "http://localhost:3000"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", labels);

        await Assert.That(routes).IsEmpty();
    }

    [Test]
    public async Task Health_check_labels_are_parsed()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            ["fennath.backend"] = "http://localhost:3000",
            ["fennath.healthcheck.path"] = "/api/health",
            ["fennath.healthcheck.interval"] = "60"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", labels);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].HealthCheckPath).IsEqualTo("/api/health");
        await Assert.That(routes[0].HealthCheckIntervalSeconds).IsEqualTo(60);
    }

    [Test]
    public async Task Health_check_labels_shared_across_comma_separated_subdomains()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "@,www",
            ["fennath.backend"] = "http://localhost:8080",
            ["fennath.healthcheck.path"] = "/health"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", labels);

        await Assert.That(routes).Count().IsEqualTo(2);
        await Assert.That(routes[0].HealthCheckPath).IsEqualTo("/health");
        await Assert.That(routes[1].HealthCheckPath).IsEqualTo("/health");
    }

    [Test]
    public async Task Invalid_health_check_interval_is_ignored()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            ["fennath.backend"] = "http://localhost:3000",
            ["fennath.healthcheck.interval"] = "not-a-number"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", labels);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].HealthCheckIntervalSeconds).IsNull();
    }

    [Test]
    public async Task Empty_subdomain_value_returns_empty()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "  ",
            ["fennath.backend"] = "http://localhost:3000"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", labels);

        await Assert.That(routes).IsEmpty();
    }
}
