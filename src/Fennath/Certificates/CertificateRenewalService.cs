using Fennath.Configuration;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Background service that ensures the wildcard certificate is provisioned on startup
/// and renewed before expiry. Checks daily.
/// </summary>
public sealed partial class CertificateRenewalService(
    AcmeService AcmeService,
    CertificateStore CertStore,
    IOptionsMonitor<FennathConfig> OptionsMonitor,
    FennathMetrics Metrics,
    TimeProvider TimeProvider,
    ILogger<CertificateRenewalService> Logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            await EnsureCertificateAsync(stoppingToken);
            var interval = OptionsMonitor.CurrentValue.Certificates.RenewalCheckIntervalSeconds;
            await Task.Delay(TimeSpan.FromSeconds(interval), TimeProvider, stoppingToken);
        } while (!stoppingToken.IsCancellationRequested);
    }

    private async Task EnsureCertificateAsync(CancellationToken ct)
    {
        using var activity = FennathMetrics.ActivitySource.StartActivity("fennath.cert-renewal-check");
        try
        {
            var wildcardHost = $"*.{OptionsMonitor.CurrentValue.EffectiveDomain}";
            var expiry = CertStore.GetExpiry();

            if (expiry is not null)
            {
                var remaining = expiry.Value - DateTime.UtcNow;
                Metrics.CertExpiryDays.Record(remaining.TotalDays,
                    new KeyValuePair<string, object?>("hostname", wildcardHost));

                var thresholdDays = OptionsMonitor.CurrentValue.Certificates.RenewalThresholdDays;
                if (remaining > TimeSpan.FromDays(thresholdDays))
                {
                    LogCertificateValid(Logger, wildcardHost, remaining.Days);
                    return;
                }

                LogCertificateRenewing(Logger, wildcardHost, remaining.Days);
            }
            else
            {
                LogCertificateProvisioning(Logger, wildcardHost);
            }

            await AcmeService.ProvisionWildcardCertificateAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Metrics.AcmeProvisioningTotal.Add(1,
                new KeyValuePair<string, object?>("result", "failure"));
            LogRenewalCheckFailed(Logger, ex);
        }
    }

    [LoggerMessage(EventId = 1110, Level = LogLevel.Debug, Message = "Certificate for {hostname} valid for {daysRemaining} more days")]
    private static partial void LogCertificateValid(ILogger logger, string hostname, int daysRemaining);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Information, Message = "Renewing certificate for {hostname} ({daysRemaining} days remaining)")]
    private static partial void LogCertificateRenewing(ILogger logger, string hostname, int daysRemaining);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Information, Message = "No certificate found for {hostname}, provisioning")]
    private static partial void LogCertificateProvisioning(ILogger logger, string hostname);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Error, Message = "Certificate renewal check failed")]
    private static partial void LogRenewalCheckFailed(ILogger logger, Exception ex);
}
