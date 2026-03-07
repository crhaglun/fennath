using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Stores the single wildcard certificate in memory and on disk.
/// Thread-safe for concurrent reads during TLS handshakes.
/// When no persisted certificate is available, serves a temporary self-signed
/// certificate until a real one is provisioned from Let's Encrypt.
/// </summary>
public sealed partial class CertificateStore : IDisposable
{
    private readonly Lock _lock = new();
    private readonly string _storagePath;
    private readonly string _domain;
    private readonly string _wildcardHost;
    private readonly ILogger<CertificateStore> _logger;

    private X509Certificate2? _certificate;

    public CertificateStore(IOptions<FennathConfig> options, ILogger<CertificateStore> logger)
    {
        _storagePath = options.Value.Certificates.StoragePath;
        _domain = options.Value.Domain;
        _wildcardHost = $"*.{_domain}";
        _logger = logger;

        Directory.CreateDirectory(_storagePath);
        LoadFromDisk();

        if (_certificate is null)
        {
            _certificate = GenerateSelfSignedCertificate(_wildcardHost);
            LogSelfSignedGenerated(_logger, _wildcardHost);
        }
    }

    /// <summary>
    /// Returns the wildcard certificate for any hostname under the configured domain.
    /// Returns null for hostnames outside our domain.
    /// </summary>
    public X509Certificate2? GetCertificate(string hostname)
    {
        if (string.Equals(hostname, _domain, StringComparison.OrdinalIgnoreCase)
            || hostname.EndsWith($".{_domain}", StringComparison.OrdinalIgnoreCase))
        {
            return _certificate;
        }

        return null;
    }

    /// <summary>
    /// Replaces the current certificate with a real one from Let's Encrypt.
    /// </summary>
    public void StoreCertificate(X509Certificate2 certificate)
    {
        lock (_lock)
        {
            var old = _certificate;
            _certificate = certificate;
            SaveToDisk(certificate);
            if (old is not null && old != certificate)
                old.Dispose();
        }

        LogCertificateStored(_logger, _wildcardHost, certificate.NotAfter);
    }

    /// <summary>
    /// Returns the expiry of the real certificate, or null if only self-signed.
    /// </summary>
    public DateTime? GetExpiry()
    {
        return _isSelfSigned ? null : _certificate?.NotAfter;
    }

    private void LoadFromDisk()
    {
        var pfxPath = Path.Combine(_storagePath, "wildcard.pfx");
        if (!File.Exists(pfxPath)) return;

        try
        {
            var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, null,
                X509KeyStorageFlags.Exportable);

            if (cert.NotAfter > DateTime.UtcNow)
            {
                _certificate = cert;
                LogCertificateLoaded(_logger, _wildcardHost, cert.NotAfter);
            }
            else
            {
                LogCertificateExpired(_logger, _wildcardHost, cert.NotAfter);
                cert.Dispose();
            }
        }
        catch (Exception ex)
        {
            LogCertificateLoadFailed(_logger, pfxPath, ex);
        }
    }

    private void SaveToDisk(X509Certificate2 certificate)
    {
        var path = Path.Combine(_storagePath, "wildcard.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
    }

    private static X509Certificate2 GenerateSelfSignedCertificate(string hostname)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            $"CN={hostname}", key, HashAlgorithmName.SHA256);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostname);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(7));

        var pfxBytes = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, null,
            X509KeyStorageFlags.Exportable);
    }

    public void Dispose()
    {
        _certificate?.Dispose();
    }

    [LoggerMessage(EventId = 1120, Level = LogLevel.Information, Message = "Certificate stored for {hostname}, expires {expiry}")]
    private static partial void LogCertificateStored(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(EventId = 1121, Level = LogLevel.Information, Message = "Loaded certificate for {hostname} from disk, expires {expiry}")]
    private static partial void LogCertificateLoaded(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(EventId = 1122, Level = LogLevel.Warning, Message = "Skipped expired certificate for {hostname} (expired {expiry})")]
    private static partial void LogCertificateExpired(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(EventId = 1123, Level = LogLevel.Warning, Message = "Failed to load certificate from {path}")]
    private static partial void LogCertificateLoadFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(EventId = 1124, Level = LogLevel.Warning, Message = "No certificate for {hostname} — using temporary self-signed certificate until Let's Encrypt provisioning completes")]
    private static partial void LogSelfSignedGenerated(ILogger logger, string hostname);
}
