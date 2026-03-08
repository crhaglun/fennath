using System.Security.Cryptography.X509Certificates;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Fennath.Configuration;
using Fennath.Dns;
using Fennath.Telemetry;
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
    FennathMetrics Metrics,
    ILogger<AcmeService> Logger)
{

    /// <summary>
    /// Provisions a certificate for the given hostnames via ACME DNS-01 challenge.
    /// </summary>
    public async Task<X509Certificate2> ProvisionCertificateAsync(
        IReadOnlyList<string> hostnames, CancellationToken ct = default)
    {
        using var activity = FennathMetrics.ActivitySource.StartActivity("fennath.acme-provision");
        activity?.SetTag("hostnames", string.Join(",", hostnames));

        var config = Options.Value;
        var acmeServer = config.Certificates.Staging
            ? WellKnownServers.LetsEncryptStagingV2
            : WellKnownServers.LetsEncryptV2;

        LogProvisioningStarted(Logger, hostnames, acmeServer);

        if (config.Certificates.Staging)
        {
            LogStagingWarning(Logger);
        }

        var acme = await GetOrCreateAcmeContextAsync(acmeServer, config);

        var order = await acme.NewOrder(hostnames.ToList());

        // Complete DNS-01 challenges, tracking created records for cleanup
        var createdChallengeSubdomains = new List<string>();
        var authorizations = await order.Authorizations();

        try
        {
            foreach (var auth in authorizations)
            {
                var challenge = await auth.Dns();
                var dnsTxt = acme.AccountKey.DnsTxt(challenge.Token);

                var authResource = await auth.Resource();
                var domain = authResource.Identifier.Value;
                var challengeSubdomain = ChallengeSubdomain(domain, config.EffectiveDomain);

                LogSettingDnsChallenge(Logger, challengeSubdomain, dnsTxt);

                await DnsProvider.CreateTxtRecordAsync(challengeSubdomain, dnsTxt, ttl: 60, ct: ct);
                createdChallengeSubdomains.Add(challengeSubdomain);

                LogWaitingForDnsPropagation(Logger, domain);
                await Task.Delay(TimeSpan.FromSeconds(config.Certificates.DnsPropagationWaitSeconds), ct);

                await challenge.Validate();
                await WaitForChallengeAsync(challenge, domain, ct);
            }
        }
        catch
        {
            await CleanupChallengeRecordsAsync(createdChallengeSubdomains, ct);
            throw;
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

        CertStore.StoreCertificate(certificate);

        await CleanupChallengeRecordsAsync(createdChallengeSubdomains, ct);

        LogProvisioningComplete(Logger, hostnames[0], certificate.NotAfter);
        Metrics.AcmeProvisioningTotal.Add(1,
            new KeyValuePair<string, object?>("result", "success"));
        return certificate;
    }

    /// <summary>
    /// Provisions a wildcard certificate for *.domain.
    /// </summary>
    public async Task<X509Certificate2> ProvisionWildcardCertificateAsync(CancellationToken ct = default)
    {
        var effectiveDomain = Options.Value.EffectiveDomain;
        return await ProvisionCertificateAsync([$"*.{effectiveDomain}", effectiveDomain], ct);
    }

    private async Task WaitForChallengeAsync(IChallengeContext challenge, string domain, CancellationToken ct)
    {
        while (true)
        {
            var resource = await challenge.Resource();
            LogChallengePolling(Logger, domain, resource.Status);

            if (resource.Status == ChallengeStatus.Valid)
            {
                LogChallengeValidated(Logger, domain);
                return;
            }

            if (resource.Status == ChallengeStatus.Invalid)
            {
                throw new InvalidOperationException(
                    $"ACME challenge failed for {domain}: {resource.Error?.Detail}");
            }

            await Task.Delay(TimeSpan.FromSeconds(Options.Value.Certificates.ChallengePollingIntervalSeconds), ct);
        }
    }

    private async Task CleanupChallengeRecordsAsync(List<string> subdomains, CancellationToken ct)
    {
        foreach (var subdomain in subdomains)
        {
            try
            {
                await DnsProvider.RemoveTxtRecordAsync(subdomain, ct);
            }
            catch (Exception ex)
            {
                LogChallengeCleanupFailed(Logger, subdomain, ex);
            }
        }
    }

    private async Task<AcmeContext> GetOrCreateAcmeContextAsync(Uri acmeServer, FennathConfig config)
    {
        var keyPath = Path.Combine(config.Certificates.StoragePath, "acme-account.pem");

        if (File.Exists(keyPath))
        {
            var pem = await File.ReadAllTextAsync(keyPath);
            var accountKey = KeyFactory.FromPem(pem);
            var acme = new AcmeContext(acmeServer, accountKey);
            await acme.Account();
            LogAccountKeyLoaded(Logger, keyPath);
            return acme;
        }

        var newAcme = new AcmeContext(acmeServer);
        await newAcme.NewAccount(config.Certificates.Email, termsOfServiceAgreed: true);

        System.IO.Directory.CreateDirectory(config.Certificates.StoragePath);
        await File.WriteAllTextAsync(keyPath, newAcme.AccountKey.ToPem());
        LogAccountKeyCreated(Logger, keyPath);

        return newAcme;
    }

    internal static string ChallengeSubdomain(string domain, string rootDomain) =>
        $"_acme-challenge.{domain}".Replace($".{rootDomain}", "");

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Starting certificate provisioning for {hostnames} via {server}")]
    private static partial void LogProvisioningStarted(ILogger logger, IReadOnlyList<string> hostnames, Uri server);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "Setting DNS-01 challenge for {subdomain}: {value}")]
    private static partial void LogSettingDnsChallenge(ILogger logger, string subdomain, string value);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Challenge validated for {domain}")]
    private static partial void LogChallengeValidated(ILogger logger, string domain);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "Failed to clean up DNS challenge record for {subdomain}")]
    private static partial void LogChallengeCleanupFailed(ILogger logger, string subdomain, Exception ex);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Information, Message = "Certificate provisioned for {hostname}, expires {expiry}")]
    private static partial void LogProvisioningComplete(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Information, Message = "Loaded existing ACME account key from {path}")]
    private static partial void LogAccountKeyLoaded(ILogger logger, string path);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Information, Message = "Created new ACME account, key saved to {path}")]
    private static partial void LogAccountKeyCreated(ILogger logger, string path);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Warning, Message = "Using Let's Encrypt STAGING — certificate will NOT be browser-trusted")]
    private static partial void LogStagingWarning(ILogger logger);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Debug, Message = "Waiting for DNS propagation for {domain}")]
    private static partial void LogWaitingForDnsPropagation(ILogger logger, string domain);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Debug, Message = "Challenge status for {domain}: {status}")]
    private static partial void LogChallengePolling(ILogger logger, string domain, ChallengeStatus? status);
}
