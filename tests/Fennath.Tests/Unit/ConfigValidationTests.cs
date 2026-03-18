using Fennath.Operator.Configuration;
using Fennath.Proxy.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Fennath.Tests.Unit;

public class ConfigValidationTests
{
    [Test]
    public async Task Valid_operator_config_passes_validation()
    {
        var host = CreateOperatorHost(config =>
        {
            config.Domain = "example.com";
            config.Dns.Loopia.Username = "user@loopiaapi";
            config.Dns.Loopia.Password = "secret";
            config.Certificates.Email = "admin@example.com";
        });

        var options = host.Services.GetRequiredService<IOptions<OperatorConfig>>();
        await Assert.That(options.Value.Domain).IsEqualTo("example.com");
    }

    [Test]
    public async Task Missing_domain_fails_proxy_validation()
    {
        var host = CreateProxyHost(config => { config.Domain = ""; });

        var options = host.Services.GetRequiredService<IOptions<ProxyConfig>>();
        await Assert.That(() => _ = options.Value).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Proxy_does_not_require_dns_credentials()
    {
        var host = CreateProxyHost(config =>
        {
            config.Domain = "example.com";
        });

        var options = host.Services.GetRequiredService<IOptions<ProxyConfig>>();
        await Assert.That(options.Value.Domain).IsEqualTo("example.com");
    }

    private static IHost CreateOperatorHost(Action<OperatorConfig> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddOptions<OperatorConfig>()
                    .Configure(configure);
                services.AddSingleton<IValidateOptions<OperatorConfig>, ValidateOperatorConfig>();
            })
            .Build();
    }

    private static IHost CreateProxyHost(Action<ProxyConfig> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddOptions<ProxyConfig>()
                    .Configure(configure);
                services.AddSingleton<IValidateOptions<ProxyConfig>, ValidateProxyConfig>();
            })
            .Build();
    }
}
