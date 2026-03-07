using Fennath.Configuration;
using Microsoft.Extensions.Configuration;

namespace Fennath.Tests.Unit;

public class ConfigValidationTests
{
    private readonly FennathConfigValidator _validator = new();

    [Test]
    public async Task Config_with_invalid_backend_url_is_rejected()
    {
        var config = new FennathConfig
        {
            Domain = "example.com",
            Routes = [new RouteEntry { Subdomain = "grafana", Backend = "not-a-url" }]
        };

        var result = _validator.Validate(null, config);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("invalid");
    }

    [Test]
    public async Task Config_with_duplicate_subdomains_is_rejected()
    {
        var config = new FennathConfig
        {
            Domain = "example.com",
            Routes =
            [
                new RouteEntry { Subdomain = "grafana", Backend = "http://localhost:3000" },
                new RouteEntry { Subdomain = "grafana", Backend = "http://localhost:4000" }
            ]
        };

        var result = _validator.Validate(null, config);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("Duplicate");
    }

    [Test]
    public async Task Valid_config_is_accepted()
    {
        var config = new FennathConfig
        {
            Domain = "example.com",
            Routes =
            [
                new RouteEntry
                {
                    Subdomain = "grafana",
                    Backend = "http://localhost:3000",
                    HealthCheck = new HealthCheckEntry { Path = "/api/health", IntervalSeconds = 60 }
                },
                new RouteEntry { Subdomain = "git", Backend = "http://192.168.1.50:3000" }
            ]
        };

        var result = _validator.Validate(null, config);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Config_binds_from_IConfiguration()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Fennath:Domain"] = "example.com",
            ["Fennath:Routes:0:Subdomain"] = "grafana",
            ["Fennath:Routes:0:Backend"] = "http://localhost:3000",
            ["Fennath:Server:HttpsPort"] = "8443",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var config = new FennathConfig { Domain = "placeholder" };
        configuration.GetSection(FennathConfig.SectionName).Bind(config);

        await Assert.That(config.Domain).IsEqualTo("example.com");
        await Assert.That(config.Routes).Count().IsEqualTo(1);
        await Assert.That(config.Routes[0].Subdomain).IsEqualTo("grafana");
        await Assert.That(config.Server.HttpsPort).IsEqualTo(8443);
    }

    [Test]
    public async Task Environment_variables_override_config()
    {
        Environment.SetEnvironmentVariable("Fennath__Dns__Loopia__Password", "secret123");

        try
        {
            var configData = new Dictionary<string, string?>
            {
                ["Fennath:Domain"] = "example.com",
                ["Fennath:Dns:Loopia:Password"] = "placeholder",
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configData)
                .AddEnvironmentVariables()
                .Build();

            var config = new FennathConfig { Domain = "placeholder" };
            configuration.GetSection(FennathConfig.SectionName).Bind(config);

            await Assert.That(config.Dns.Loopia.Password).IsEqualTo("secret123");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Fennath__Dns__Loopia__Password", null);
        }
    }

    [Test]
    public async Task Default_values_are_applied_when_not_specified()
    {
        var config = new FennathConfig { Domain = "example.com" };

        await Assert.That(config.Server.HttpsPort).IsEqualTo(443);
        await Assert.That(config.Server.HttpPort).IsEqualTo(80);
        await Assert.That(config.Server.HttpToHttpsRedirect).IsTrue();
        await Assert.That(config.Certificates.Wildcard).IsTrue();
        await Assert.That(config.Certificates.Staging).IsFalse();
        await Assert.That(config.Dns.PublicIpCheckIntervalSeconds).IsEqualTo(300);
        await Assert.That(config.Dns.IpEchoServices).Count().IsEqualTo(3);
    }
}
