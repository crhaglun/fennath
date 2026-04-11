using System.Security.Cryptography.X509Certificates;
using Fennath.Operator.Configuration;
using Fennath.Telemetry;
using Microsoft.Extensions.Options;

namespace Fennath.Operator.Certificates;

/// <summary>
/// Background service that ensures the wildcard certificate is provisioned on startup
/// and renewed before expiry. Owns the certificate file on disk — reads it on startup,
/// writes it after provisioning, and caches NotAfter for renewal decisions.
/// </summary>
public sealed partial class CertificateRenewalService(
    AcmeService AcmeService,
    IOptionsMonitor<OperatorConfig> OptionsMonitor,
    FennathMetrics Metrics,
    TimeProvider TimeProvider,
    ILogger<CertificateRenewalService> Logger) : BackgroundService
{
    private DateTime? _certNotAfter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _certNotAfter = ReadCertExpiryFromDisk();

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
            var config = OptionsMonitor.CurrentValue;
            var wildcardHost = $"*.{config.EffectiveDomain}";

            if (_certNotAfter is null)
            {
                LogCertificateProvisioning(Logger, wildcardHost);
            }
            else
            {
                var remaining = _certNotAfter.Value - DateTime.UtcNow;
                Metrics.CertExpiryDays.Record(remaining.TotalDays,
                    new KeyValuePair<string, object?>("hostname", wildcardHost));

                var thresholdDays = config.Certificates.RenewalThresholdDays;
                if (remaining > TimeSpan.FromDays(thresholdDays))
                {
                    LogCertificateValid(Logger, wildcardHost, remaining.Days);
                    return;
                }

                LogCertificateRenewing(Logger, wildcardHost, remaining.Days);
            }

            using var cert = await AcmeService.ProvisionWildcardCertificateAsync(ct);
            SaveCertificateToDisk(cert);
            _certNotAfter = cert.NotAfter;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Metrics.AcmeProvisioningTotal.Add(1,
                new KeyValuePair<string, object?>("result", "failure"));
            LogRenewalCheckFailed(Logger, ex);
        }
    }

    private DateTime? ReadCertExpiryFromDisk()
    {
        var config = OptionsMonitor.CurrentValue;
        var path = CertFilePath(config);

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(path, null,
                X509KeyStorageFlags.Exportable);

            if (cert.NotAfter <= DateTime.UtcNow)
            {
                LogCertificateExpiredOnDisk(Logger, path);
                return null;
            }

            return cert.NotAfter;
        }
        catch (Exception ex)
        {
            LogCertificateReadFailed(Logger, path, ex);
            return null;
        }
    }

    private void SaveCertificateToDisk(X509Certificate2 certificate)
    {
        var config = OptionsMonitor.CurrentValue;
        var path = CertFilePath(config);
        var tempPath = path + ".tmp";
        Directory.CreateDirectory(config.Certificates.StoragePath);
        File.WriteAllBytes(tempPath, certificate.Export(X509ContentType.Pfx));
        File.Move(tempPath, path, overwrite: true);
        LogCertificateStored(Logger, path, certificate.NotAfter);
    }

    private static string CertFilePath(OperatorConfig config) =>
        Path.Combine(config.Certificates.StoragePath, $"{config.EffectiveDomain}.pfx");

    [LoggerMessage(EventId = 1110, Level = LogLevel.Debug, Message = "Certificate for {hostname} valid for {daysRemaining} more days")]
    private static partial void LogCertificateValid(ILogger logger, string hostname, int daysRemaining);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Information, Message = "Renewing certificate for {hostname} ({daysRemaining} days remaining)")]
    private static partial void LogCertificateRenewing(ILogger logger, string hostname, int daysRemaining);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Information, Message = "No certificate found for {hostname}, provisioning")]
    private static partial void LogCertificateProvisioning(ILogger logger, string hostname);

    [LoggerMessage(EventId = 1113, Level = LogLevel.Information, Message = "Certificate stored at {path}, expires {expiry}")]
    private static partial void LogCertificateStored(ILogger logger, string path, DateTime expiry);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Error, Message = "Certificate renewal check failed")]
    private static partial void LogRenewalCheckFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1115, Level = LogLevel.Warning, Message = "Expired certificate found on disk: {path}")]
    private static partial void LogCertificateExpiredOnDisk(ILogger logger, string path);

    [LoggerMessage(EventId = 1116, Level = LogLevel.Warning, Message = "Failed to read certificate from {path}")]
    private static partial void LogCertificateReadFailed(ILogger logger, string path, Exception ex);
}
