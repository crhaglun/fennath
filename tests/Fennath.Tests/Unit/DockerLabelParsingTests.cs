using Fennath.Operator.Discovery;

namespace Fennath.Tests.Unit;

public class DockerLabelParsingTests
{
    [Test]
    public async Task Parses_single_subdomain_deriving_backend_from_container_name()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", "grafana-app", labels);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].Subdomain).IsEqualTo("grafana");
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://grafana-app:80");
        await Assert.That(routes[0].Source).IsEqualTo("docker:abc123");
    }

    [Test]
    public async Task Custom_port_label_overrides_default()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            ["fennath.port"] = "3000"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", "grafana-app", labels);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://grafana-app:3000");
    }

    [Test]
    public async Task Parses_comma_separated_subdomains()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "@, www"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("def456", "my-app", labels);

        await Assert.That(routes).Count().IsEqualTo(2);
        await Assert.That(routes[0].Subdomain).IsEqualTo("@");
        await Assert.That(routes[0].IsApex).IsTrue();
        await Assert.That(routes[1].Subdomain).IsEqualTo("www");
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://my-app:80");
        await Assert.That(routes[1].BackendUrl).IsEqualTo("http://my-app:80");
    }

    [Test]
    public async Task Missing_subdomain_label_returns_empty()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.port"] = "8080"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", "my-app", labels);

        await Assert.That(routes).IsEmpty();
    }

    [Test]
    public async Task Invalid_port_label_uses_default()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            ["fennath.port"] = "not-a-number"
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", "grafana-app", labels);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://grafana-app:80");
    }

    [Test]
    public async Task Empty_subdomain_value_returns_empty()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "  "
        };

        var routes = DockerRouteDiscovery.ParseContainerRoutes("abc123", "my-app", labels);

        await Assert.That(routes).IsEmpty();
    }
}
