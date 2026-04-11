using Fennath.Discovery;
using Fennath.Operator.Configuration;
using Fennath.Operator.Discovery;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fennath.Tests.Unit;

public class StaticRouteDiscoveryTests
{
    private static readonly NullLogger<StaticRouteDiscovery> Logger = new();

    [Test]
    public async Task Parses_single_static_route()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "nas", BackendUrl = "http://192.168.1.50:5000" }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].Subdomain).IsEqualTo("nas");
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://192.168.1.50:5000");
        await Assert.That(routes[0].Source).IsEqualTo("static");
    }

    [Test]
    public async Task Parses_multiple_static_routes()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "nas", BackendUrl = "http://192.168.1.50:5000" },
            new() { Subdomain = "pve", BackendUrl = "https://192.168.1.10:8006" }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).Count().IsEqualTo(2);
        await Assert.That(routes[0].Subdomain).IsEqualTo("nas");
        await Assert.That(routes[1].Subdomain).IsEqualTo("pve");
    }

    [Test]
    public async Task Apex_marker_is_supported()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "@", BackendUrl = "http://192.168.1.50:80" }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].IsApex).IsTrue();
    }

    [Test]
    public async Task Empty_config_produces_empty_routes()
    {
        var routes = StaticRouteDiscovery.BuildRoutes([], Logger);

        await Assert.That(routes).IsEmpty();
    }

    [Test]
    public async Task Skips_entry_with_empty_subdomain()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "", BackendUrl = "http://192.168.1.50:5000" },
            new() { Subdomain = "  ", BackendUrl = "http://192.168.1.50:5000" }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).IsEmpty();
    }

    [Test]
    public async Task Skips_entry_with_invalid_backend_url()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "nas", BackendUrl = "not-a-url" },
            new() { Subdomain = "pve", BackendUrl = "" },
            new() { Subdomain = "vm", BackendUrl = "ftp://server/file" }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).IsEmpty();
    }

    [Test]
    public async Task Skips_duplicate_subdomains_keeping_first()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "nas", BackendUrl = "http://192.168.1.50:5000" },
            new() { Subdomain = "NAS", BackendUrl = "http://192.168.1.99:5000" }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://192.168.1.50:5000");
    }

    [Test]
    public async Task Trims_whitespace_from_subdomain_and_backend()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "  nas  ", BackendUrl = "  http://192.168.1.50:5000  " }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].Subdomain).IsEqualTo("nas");
        await Assert.That(routes[0].BackendUrl).IsEqualTo("http://192.168.1.50:5000");
    }

    [Test]
    public async Task Https_backend_url_is_accepted()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "pve", BackendUrl = "https://192.168.1.10:8006" }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].BackendUrl).IsEqualTo("https://192.168.1.10:8006");
    }

    [Test]
    public async Task Valid_entries_survive_alongside_invalid_ones()
    {
        var entries = new List<StaticRouteEntry>
        {
            new() { Subdomain = "", BackendUrl = "http://bad:80" },
            new() { Subdomain = "good", BackendUrl = "http://192.168.1.50:80" },
            new() { Subdomain = "bad", BackendUrl = "not-a-url" }
        };

        var routes = StaticRouteDiscovery.BuildRoutes(entries, Logger);

        await Assert.That(routes).Count().IsEqualTo(1);
        await Assert.That(routes[0].Subdomain).IsEqualTo("good");
    }

    [Test]
    public async Task RoutesEqual_returns_true_for_identical_lists()
    {
        var a = new List<DiscoveredRoute> { new("nas", "http://host:80", "static") };
        var b = new List<DiscoveredRoute> { new("nas", "http://host:80", "static") };

        await Assert.That(StaticRouteDiscovery.RoutesEqual(a, b)).IsTrue();
    }

    [Test]
    public async Task RoutesEqual_returns_false_for_different_counts()
    {
        var a = new List<DiscoveredRoute> { new("nas", "http://host:80", "static") };
        var b = new List<DiscoveredRoute>();

        await Assert.That(StaticRouteDiscovery.RoutesEqual(a, b)).IsFalse();
    }

    [Test]
    public async Task RoutesEqual_returns_false_for_different_content()
    {
        var a = new List<DiscoveredRoute> { new("nas", "http://host1:80", "static") };
        var b = new List<DiscoveredRoute> { new("nas", "http://host2:80", "static") };

        await Assert.That(StaticRouteDiscovery.RoutesEqual(a, b)).IsFalse();
    }
}
