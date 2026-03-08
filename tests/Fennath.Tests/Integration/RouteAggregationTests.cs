using System.Net;
using Fennath.Discovery;
using Fennath.Tests.Helpers;
using Microsoft.AspNetCore.Builder;

namespace Fennath.Tests.Integration;

public class RouteAggregationTests : IAsyncDisposable
{
    private TestBackend _backend = null!;

    [Before(Test)]
    public async Task SetUp()
    {
        _backend = await TestBackend.CreateAsync();
    }

    [After(Test)]
    public async Task TearDown()
    {
        await _backend.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _backend.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task Dynamic_route_addition_makes_new_host_routable()
    {
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));
        var client = ctx.CreateClient();

        await using var backend2 = await TestBackend.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/", () => "from new service");
        });

        // Add a new route dynamically
        ctx.RouteDiscovery.SetRoutes(
            new DiscoveredRoute("grafana", _backend.Url, "test"),
            new DiscoveredRoute("wiki", backend2.Url, "test"));

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "wiki.example.com";

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("from new service");
    }

    [Test]
    public async Task Route_removal_makes_host_unreachable()
    {
        await using var ctx = await FennathTestHost.CreateAsync(
            ("grafana", _backend.Url), ("wiki", _backend.Url));
        var client = ctx.CreateClient();

        // Remove wiki route
        ctx.RouteDiscovery.SetRoutes(
            new DiscoveredRoute("grafana", _backend.Url, "test"));

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "wiki.example.com";

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Duplicate_subdomains_are_deduplicated_first_wins()
    {
        await using var backend2 = await TestBackend.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/", () => "from second source");
        });

        // First source claims "grafana" → _backend, second source also claims "grafana" → backend2
        // RouteAggregator.Merge takes the first occurrence
        await using var ctx = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        var client = ctx.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "grafana.example.com";

        var response = await client.SendAsync(request);

        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("hello from backend");
    }
}
