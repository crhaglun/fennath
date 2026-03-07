using Fennath.Configuration;
using Microsoft.Extensions.Configuration;

namespace Fennath.Tests.Unit;

public class ConfigValidationTests
{
    private readonly FennathConfigValidator _validator = new();

    [Test]
    public async Task Valid_config_is_accepted()
    {
        var config = new FennathConfig { Domain = "example.com" };

        var result = _validator.Validate(null, config);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Config_binds_from_IConfiguration()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Fennath:Domain"] = "example.com",
            ["Fennath:Server:HttpsPort"] = "8443",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var config = new FennathConfig { Domain = "placeholder" };
        configuration.GetSection(FennathConfig.SectionName).Bind(config);

        await Assert.That(config.Domain).IsEqualTo("example.com");
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
        await Assert.That(config.Certificates.Staging).IsFalse();
        await Assert.That(config.Dns.PublicIpCheckIntervalSeconds).IsEqualTo(300);
        await Assert.That(config.Dns.IpEchoServices).Count().IsEqualTo(3);
    }
}
