using System.Net;
using System.Text;
using Fennath.Configuration;
using Fennath.Operator.Dns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fennath.Tests.Unit;

public class PublicIpResolverTests
{
    [Test]
    public async Task Returns_ip_from_first_successful_service()
    {
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            ["https://api.ipify.org"] = "1.2.3.4",
        });

        var resolver = CreateResolver(handler);

        var ip = await resolver.GetPublicIpAsync();

        await Assert.That(ip).IsEqualTo("1.2.3.4");
    }

    [Test]
    public async Task Trims_whitespace_from_response()
    {
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            ["https://api.ipify.org"] = "  5.6.7.8\n",
        });

        var resolver = CreateResolver(handler);

        var ip = await resolver.GetPublicIpAsync();

        await Assert.That(ip).IsEqualTo("5.6.7.8");
    }

    [Test]
    public async Task Falls_back_to_second_service_when_first_fails()
    {
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            ["https://icanhazip.com"] = "9.8.7.6",
        });

        var resolver = CreateResolver(handler);

        var ip = await resolver.GetPublicIpAsync();

        await Assert.That(ip).IsEqualTo("9.8.7.6");
    }

    [Test]
    public async Task Skips_service_returning_non_ip_content()
    {
        var handler = new FakeHandler(new Dictionary<string, string>
        {
            ["https://api.ipify.org"] = "<html>error</html>",
            ["https://icanhazip.com"] = "10.20.30.40",
        });

        var resolver = CreateResolver(handler);

        var ip = await resolver.GetPublicIpAsync();

        await Assert.That(ip).IsEqualTo("10.20.30.40");
    }

    [Test]
    public async Task Throws_AggregateException_when_all_services_fail()
    {
        var handler = new FakeHandler([]); // All services will 404

        var resolver = CreateResolver(handler);

        await Assert.That(async () => await resolver.GetPublicIpAsync())
            .ThrowsExactly<AggregateException>();
    }

    private static PublicIpResolver CreateResolver(HttpMessageHandler handler)
    {
        var config = new FennathConfig
        {
            Domain = "example.com",
            Dns = new DnsConfig
            {
                IpEchoServices =
                [
                    "https://api.ipify.org",
                    "https://icanhazip.com",
                    "https://checkip.amazonaws.com",
                ]
            }
        };

        var httpClient = new HttpClient(handler);
        return new PublicIpResolver(httpClient, Options.Create(config), NullLogger<PublicIpResolver>.Instance);
    }

    private sealed class FakeHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri?.ToString().TrimEnd('/') ?? "";

            if (responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/plain")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
