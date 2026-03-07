using System.Net;
using Fennath.Tests.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Fennath.Tests.Integration;

public class ProxyRoutingTests : IAsyncDisposable
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
    public async Task Request_with_matching_host_header_is_proxied_to_backend()
    {
        using var fennath = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        var client = fennath.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "grafana.example.com";

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).IsEqualTo("hello from backend");
    }

    [Test]
    public async Task Request_with_unknown_host_returns_not_found()
    {
        using var fennath = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        var client = fennath.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "unknown.example.com";

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Healthz_endpoint_returns_healthy()
    {
        using var fennath = await FennathTestHost.CreateAsync(("grafana", _backend.Url));

        var client = fennath.GetTestClient();

        var response = await client.GetAsync("/healthz");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Multiple_routes_proxy_to_correct_backends()
    {
        await using var backend2 = await TestBackend.CreateAsync(endpoints =>
        {
            endpoints.MapGet("/", () => "hello from backend2");
        });

        using var fennath = await FennathTestHost.CreateAsync(
            ("grafana", _backend.Url),
            ("git", backend2.Url));

        var client = fennath.GetTestClient();

        var req1 = new HttpRequestMessage(HttpMethod.Get, "/");
        req1.Headers.Host = "grafana.example.com";
        var resp1 = await client.SendAsync(req1);
        await Assert.That(await resp1.Content.ReadAsStringAsync()).IsEqualTo("hello from backend");

        var req2 = new HttpRequestMessage(HttpMethod.Get, "/");
        req2.Headers.Host = "git.example.com";
        var resp2 = await client.SendAsync(req2);
        await Assert.That(await resp2.Content.ReadAsStringAsync()).IsEqualTo("hello from backend2");
    }

    [Test]
    public async Task Apex_route_matches_bare_domain()
    {
        using var fennath = await FennathTestHost.CreateAsync(("@", _backend.Url));

        var client = fennath.GetTestClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "example.com";

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).IsEqualTo("hello from backend");
    }
}
