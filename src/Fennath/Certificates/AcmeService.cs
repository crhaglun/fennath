using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Fennath.Configuration;
using Fennath.Dns;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// ACME v2 certificate provisioning via Certes.
/// Uses DNS-01 challenges through IDnsProvider.
/// Supports both Let's Encrypt production and staging.
/// </summary>
public sealed partial class AcmeService(
    IDnsProvider DnsProvider,
    CertificateStore CertStore,
    IOptions<FennathConfig> Options,
    ILogger<AcmeService> Logger)
{
    /// <summary>
    /// Provisions a certificate for the given hostnames via ACME DNS-01 challenge.
    /// </summary>
    public async Task<X509Certificate2> ProvisionCertificateAsync(
        IReadOnlyList<string> hostnames, CancellationToken ct = default)
    {
        var config = Options.Value;
        var acmeServer = config.Certificates.Staging
            ? WellKnownServers.LetsEncryptStagingV2
            : WellKnownServers.LetsEncryptV2;

        LogProvisioningStarted(Logger, hostnames, acmeServer);

        var acme = new AcmeContext(acmeServer);
        await acme.NewAccount(config.Certificates.Email, termsOfServiceAgreed: true);

        var order = await acme.NewOrder(hostnames.ToList());

        // Complete DNS-01 challenges
        var authorizations = await order.Authorizations();
        foreach (var auth in authorizations)
        {
            var challenge = await auth.Dns();
            var dnsTxt = acme.AccountKey.DnsTxt(challenge.Token);

            var authResource = await auth.Resource();
            var domain = authResource.Identifier.Value;
            var challengeSubdomain = $"_acme-challenge.{domain}".Replace($".{config.Domain}", "");

            LogSettingDnsChallenge(Logger, challengeSubdomain, dnsTxt);

            await DnsProvider.CreateTxtRecordAsync(challengeSubdomain, dnsTxt, ttl: 60, ct: ct);

            // Wait for DNS propagation
            await Task.Delay(TimeSpan.FromSeconds(30), ct);

            var challengeResult = await challenge.Validate();
            var status = challengeResult.Status?.ToString() ?? "unknown";
            LogChallengeValidated(Logger, domain, status);
        }

        // Generate certificate
        var privateKey = KeyFactory.NewKey(Certes.KeyAlgorithm.ES256);
        var certChain = await order.Generate(
            new CsrInfo { CommonName = hostnames[0] },
            privateKey);

        var pfxBuilder = certChain.ToPfx(privateKey);
        var pfxBytes = pfxBuilder.Build(hostnames[0], "");

        var certificate = X509CertificateLoader.LoadPkcs12(pfxBytes, null,
            X509KeyStorageFlags.Exportable);

        // Store for each hostname
        foreach (var hostname in hostnames)
        {
            CertStore.StoreCertificate(hostname, certificate);
        }

        // Clean up DNS challenge records
        foreach (var auth in authorizations)
        {
            var authResource = await auth.Resource();
            var domain = authResource.Identifier.Value;
            var challengeSubdomain = $"_acme-challenge.{domain}".Replace($".{config.Domain}", "");

            try
            {
                await DnsProvider.RemoveTxtRecordAsync(challengeSubdomain, ct);
            }
            catch (Exception ex)
            {
                LogChallengeCleanupFailed(Logger, challengeSubdomain, ex);
            }
        }

        LogProvisioningComplete(Logger, hostnames[0], certificate.NotAfter);
        return certificate;
    }

    /// <summary>
    /// Provisions a wildcard certificate for *.domain.
    /// </summary>
    public async Task<X509Certificate2> ProvisionWildcardCertificateAsync(CancellationToken ct = default)
    {
        var domain = Options.Value.Domain;
        return await ProvisionCertificateAsync([$"*.{domain}", domain], ct);
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Starting certificate provisioning for {hostnames} via {server}")]
    private static partial void LogProvisioningStarted(ILogger logger, IReadOnlyList<string> hostnames, Uri server);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Setting DNS-01 challenge for {subdomain}: {value}")]
    private static partial void LogSettingDnsChallenge(ILogger logger, string subdomain, string value);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Challenge validated for {domain}: {status}")]
    private static partial void LogChallengeValidated(ILogger logger, string domain, string status);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "Failed to clean up DNS challenge record for {subdomain}")]
    private static partial void LogChallengeCleanupFailed(ILogger logger, string subdomain, Exception ex);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Information, Message = "Certificate provisioned for {hostname}, expires {expiry}")]
    private static partial void LogProvisioningComplete(ILogger logger, string hostname, DateTime expiry);
}
