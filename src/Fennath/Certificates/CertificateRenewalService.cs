using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Background service that ensures certificates are provisioned on startup
/// and renewed before expiry. Checks daily for certificates expiring within 30 days.
/// </summary>
public sealed partial class CertificateRenewalService : BackgroundService
{
    private static readonly TimeSpan RenewalThreshold = TimeSpan.FromDays(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly AcmeService _acmeService;
    private readonly CertificateStore _certStore;
    private readonly IOptionsMonitor<FennathConfig> _optionsMonitor;
    private readonly ILogger<CertificateRenewalService> _logger;

    public CertificateRenewalService(
        AcmeService acmeService,
        CertificateStore certStore,
        IOptionsMonitor<FennathConfig> optionsMonitor,
        ILogger<CertificateRenewalService> logger)
    {
        _acmeService = acmeService;
        _certStore = certStore;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial provisioning
        await EnsureCertificatesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CheckInterval, stoppingToken);
            await EnsureCertificatesAsync(stoppingToken);
        }
    }

    private async Task EnsureCertificatesAsync(CancellationToken ct)
    {
        try
        {
            var config = _optionsMonitor.CurrentValue;
            var expiries = _certStore.GetCertificateExpiries();

            if (config.Certificates.Wildcard)
            {
                var wildcardHost = $"*.{config.Domain}";
                await EnsureCertificateAsync(wildcardHost, expiries, ct);
            }
            else
            {
                // Per-subdomain certificates
                foreach (var route in config.Routes)
                {
                    var host = $"{route.Subdomain}.{config.Domain}";
                    await EnsureCertificateAsync(host, expiries, ct);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRenewalCheckFailed(_logger, ex);
        }
    }

    private async Task EnsureCertificateAsync(
        string hostname,
        IReadOnlyDictionary<string, DateTime> expiries,
        CancellationToken ct)
    {
        if (expiries.TryGetValue(hostname, out var expiry))
        {
            var remaining = expiry - DateTime.UtcNow;
            if (remaining > RenewalThreshold)
            {
                LogCertificateValid(_logger, hostname, remaining.Days);
                return;
            }

            LogCertificateRenewing(_logger, hostname, remaining.Days);
        }
        else
        {
            LogCertificateProvisioning(_logger, hostname);
        }

        try
        {
            if (hostname.StartsWith('*'))
            {
                await _acmeService.ProvisionWildcardCertificateAsync(ct);
            }
            else
            {
                await _acmeService.ProvisionCertificateAsync([hostname], ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProvisioningFailed(_logger, hostname, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Certificate for {hostname} valid for {daysRemaining} more days")]
    private static partial void LogCertificateValid(ILogger logger, string hostname, int daysRemaining);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renewing certificate for {hostname} ({daysRemaining} days remaining)")]
    private static partial void LogCertificateRenewing(ILogger logger, string hostname, int daysRemaining);

    [LoggerMessage(Level = LogLevel.Information, Message = "No certificate found for {hostname}, provisioning")]
    private static partial void LogCertificateProvisioning(ILogger logger, string hostname);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to provision/renew certificate for {hostname}")]
    private static partial void LogProvisioningFailed(ILogger logger, string hostname, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Certificate renewal check failed")]
    private static partial void LogRenewalCheckFailed(ILogger logger, Exception ex);
}
