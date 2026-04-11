using Fennath.Operator.Discovery;

namespace Fennath.Tests.Unit;

/// <summary>
/// Tests for the <c>fennath.domain</c> label filtering that enables multi-operator deployments.
/// </summary>
public class DomainFilterTests
{
    [Test]
    public async Task Container_without_domain_label_is_not_claimed()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana"
        };

        var claimed = DockerRouteDiscovery.IsClaimedByThisOperator(labels, "lab.example.com", "grafana");

        await Assert.That(claimed).IsFalse();
    }

    [Test]
    public async Task Container_with_empty_domain_label_is_not_claimed()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            [DockerRouteDiscovery.DomainLabel] = "  "
        };

        var claimed = DockerRouteDiscovery.IsClaimedByThisOperator(labels, "lab.example.com", "grafana");

        await Assert.That(claimed).IsFalse();
    }

    [Test]
    public async Task Container_with_matching_domain_label_is_claimed()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            [DockerRouteDiscovery.DomainLabel] = "lab.example.com"
        };

        var claimed = DockerRouteDiscovery.IsClaimedByThisOperator(labels, "lab.example.com", "grafana");

        await Assert.That(claimed).IsTrue();
    }

    [Test]
    public async Task Container_with_non_matching_domain_label_is_not_claimed()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            [DockerRouteDiscovery.DomainLabel] = "apps.example.org"
        };

        var claimed = DockerRouteDiscovery.IsClaimedByThisOperator(labels, "lab.example.com", "grafana");

        await Assert.That(claimed).IsFalse();
    }

    [Test]
    public async Task Domain_matching_is_case_insensitive()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            [DockerRouteDiscovery.DomainLabel] = "Lab.Example.COM"
        };

        var claimed = DockerRouteDiscovery.IsClaimedByThisOperator(labels, "lab.example.com", "grafana");

        await Assert.That(claimed).IsTrue();
    }

    [Test]
    public async Task Domain_label_whitespace_is_trimmed()
    {
        var labels = new Dictionary<string, string>
        {
            ["fennath.subdomain"] = "grafana",
            [DockerRouteDiscovery.DomainLabel] = "  lab.example.com  "
        };

        var claimed = DockerRouteDiscovery.IsClaimedByThisOperator(labels, "lab.example.com", "grafana");

        await Assert.That(claimed).IsTrue();
    }
}
