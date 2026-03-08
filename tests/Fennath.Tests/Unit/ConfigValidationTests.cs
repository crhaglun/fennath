using Fennath.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Fennath.Tests.Unit;

public class ConfigValidationTests
{
    [Test]
    public async Task Valid_config_passes_validation()
    {
        var host = CreateHostWithConfig(config =>
        {
            config.Domain = "example.com";
            config.Dns.Loopia.Username = "user@loopiaapi";
            config.Dns.Loopia.Password = "secret";
            config.Certificates.Email = "admin@example.com";
        });

        var options = host.Services.GetRequiredService<IOptions<FennathConfig>>();
        var value = options.Value;
        await Assert.That(value.Domain).IsEqualTo("example.com");
    }
    [Test]
    public async Task Missing_domain_fails_validation()
    {
        var host = CreateHostWithConfig(config =>
        {
            config.Dns.Loopia.Username = "user@loopiaapi";
            config.Dns.Loopia.Password = "secret";
            config.Certificates.Email = "admin@example.com";
        });

        var options = host.Services.GetRequiredService<IOptions<FennathConfig>>();
        await Assert.That(() => _ = options.Value).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Empty_loopia_username_fails_validation()
    {
        var host = CreateHostWithConfig(config =>
        {
            config.Domain = "example.com";
            config.Dns.Loopia.Username = "";
            config.Dns.Loopia.Password = "secret";
            config.Certificates.Email = "admin@example.com";
        });

        var options = host.Services.GetRequiredService<IOptions<FennathConfig>>();
        await Assert.That(() => _ = options.Value).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Empty_loopia_password_fails_validation()
    {
        var host = CreateHostWithConfig(config =>
        {
            config.Domain = "example.com";
            config.Dns.Loopia.Username = "user@loopiaapi";
            config.Dns.Loopia.Password = "";
            config.Certificates.Email = "admin@example.com";
        });

        var options = host.Services.GetRequiredService<IOptions<FennathConfig>>();
        await Assert.That(() => _ = options.Value).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Empty_certificate_email_fails_validation()
    {
        var host = CreateHostWithConfig(config =>
        {
            config.Domain = "example.com";
            config.Dns.Loopia.Username = "user@loopiaapi";
            config.Dns.Loopia.Password = "secret";
            config.Certificates.Email = "";
        });

        var options = host.Services.GetRequiredService<IOptions<FennathConfig>>();
        await Assert.That(() => _ = options.Value).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Zero_ip_check_interval_fails_validation()
    {
        var host = CreateHostWithConfig(config =>
        {
            config.Domain = "example.com";
            config.Dns.Loopia.Username = "user@loopiaapi";
            config.Dns.Loopia.Password = "secret";
            config.Dns.PublicIpCheckIntervalSeconds = 0;
            config.Certificates.Email = "admin@example.com";
        });

        var options = host.Services.GetRequiredService<IOptions<FennathConfig>>();
        await Assert.That(() => _ = options.Value).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Negative_renewal_check_interval_fails_validation()
    {
        var host = CreateHostWithConfig(config =>
        {
            config.Domain = "example.com";
            config.Dns.Loopia.Username = "user@loopiaapi";
            config.Dns.Loopia.Password = "secret";
            config.Certificates.Email = "admin@example.com";
            config.Certificates.RenewalCheckIntervalSeconds = -1;
        });

        var options = host.Services.GetRequiredService<IOptions<FennathConfig>>();
        await Assert.That(() => _ = options.Value).Throws<OptionsValidationException>();
    }

    [Test]
    public async Task Port_zero_fails_validation()
    {
        var host = CreateHostWithConfig(config =>
        {
            config.Domain = "example.com";
            config.Dns.Loopia.Username = "user@loopiaapi";
            config.Dns.Loopia.Password = "secret";
            config.Certificates.Email = "admin@example.com";
            config.Server.HttpsPort = 0;
        });

        var options = host.Services.GetRequiredService<IOptions<FennathConfig>>();
        await Assert.That(() => _ = options.Value).Throws<OptionsValidationException>();
    }

    private static IHost CreateHostWithConfig(Action<FennathConfig> configure)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddOptions<FennathConfig>()
                    .Configure(configure);
                services.AddSingleton<IValidateOptions<FennathConfig>, FennathConfigValidator>();
            })
            .Build();
    }
}
