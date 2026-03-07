using Fennath.Configuration;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Background service that ensures certificates are provisioned on startup
/// and renewed before expiry. Checks daily for certificates expiring within 30 days.
/// </summary>
public sealed partial class CertificateRenewalService(
    AcmeService AcmeService,
    CertificateStore CertStore,
    IOptionsMonitor<FennathConfig> OptionsMonitor,
    FennathMetrics Metrics,
    ILogger<CertificateRenewalService> Logger) : BackgroundService
{
    private static readonly TimeSpan RenewalThreshold = TimeSpan.FromDays(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            await EnsureCertificatesAsync(stoppingToken);
            await Task.Delay(CheckInterval, stoppingToken);
        } while (!stoppingToken.IsCancellationRequested);
    }

    private async Task EnsureCertificatesAsync(CancellationToken ct)
    {
        try
        {
            var config = OptionsMonitor.CurrentValue;
            var expiries = CertStore.GetCertificateExpiries();

            // Report cert expiry metrics for all known certificates
            foreach (var (hostname, expiry) in expiries)
            {
                var daysRemaining = (expiry - DateTime.UtcNow).TotalDays;
                Metrics.CertExpiryDays.Record(daysRemaining,
                    new KeyValuePair<string, object?>("hostname", hostname));
            }

            var wildcardHost = $"*.{config.Domain}";
            await EnsureCertificateAsync(wildcardHost, expiries, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRenewalCheckFailed(Logger, ex);
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
                LogCertificateValid(Logger, hostname, remaining.Days);
                return;
            }

            LogCertificateRenewing(Logger, hostname, remaining.Days);
        }
        else
        {
            LogCertificateProvisioning(Logger, hostname);
        }

        try
        {
            if (hostname.StartsWith('*'))
            {
                await AcmeService.ProvisionWildcardCertificateAsync(ct);
            }
            else
            {
                await AcmeService.ProvisionCertificateAsync([hostname], ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProvisioningFailed(Logger, hostname, ex);
        }
    }

    [LoggerMessage(EventId = 1110, Level = LogLevel.Debug, Message = "Certificate for {hostname} valid for {daysRemaining} more days")]
    private static partial void LogCertificateValid(ILogger logger, string hostname, int daysRemaining);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Information, Message = "Renewing certificate for {hostname} ({daysRemaining} days remaining)")]
    private static partial void LogCertificateRenewing(ILogger logger, string hostname, int daysRemaining);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Information, Message = "No certificate found for {hostname}, provisioning")]
    private static partial void LogCertificateProvisioning(ILogger logger, string hostname);

    [LoggerMessage(EventId = 1113, Level = LogLevel.Error, Message = "Failed to provision/renew certificate for {hostname}")]
    private static partial void LogProvisioningFailed(ILogger logger, string hostname, Exception ex);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Error, Message = "Certificate renewal check failed")]
    private static partial void LogRenewalCheckFailed(ILogger logger, Exception ex);
}
