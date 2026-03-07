using System.Security.Cryptography.X509Certificates;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Stores the single wildcard certificate in memory and on disk.
/// Thread-safe for concurrent reads during TLS handshakes.
/// The application blocks on startup until a valid certificate is available.
/// </summary>
public sealed partial class CertificateStore : IDisposable
{
    private readonly Lock _lock = new();
    private readonly string _storagePath;
    private readonly string _wildcardHost;
    private readonly ILogger<CertificateStore> _logger;

    private X509Certificate2? _certificate;

    public CertificateStore(IOptions<FennathConfig> options, ILogger<CertificateStore> logger)
    {
        _storagePath = options.Value.Certificates.StoragePath;
        _wildcardHost = $"*.{options.Value.Domain}";
        _logger = logger;

        Directory.CreateDirectory(_storagePath);
        LoadFromDisk();
    }

    /// <summary>
    /// Returns the current certificate, or null if none is loaded.
    /// </summary>
    public X509Certificate2? GetCertificate() => _certificate;

    /// <summary>
    /// Stores a certificate from Let's Encrypt, replacing any existing one.
    /// </summary>
    public void StoreCertificate(X509Certificate2 certificate)
    {
        lock (_lock)
        {
            var old = _certificate;
            _certificate = certificate;

            try
            {
                SaveToDisk(certificate);
            }
            catch (Exception ex)
            {
                LogDiskWriteFailed(_logger, ex);
            }

            if (old is not null && old != certificate)
            {
                old.Dispose();
            }
        }

        LogCertificateStored(_logger, _wildcardHost, certificate.NotAfter);
    }

    /// <summary>
    /// Returns the expiry of the certificate, or null if none is loaded.
    /// </summary>
    public DateTime? GetExpiry() => _certificate?.NotAfter;

    private void LoadFromDisk()
    {
        var pfxPath = Path.Combine(_storagePath, "wildcard.pfx");
        if (!File.Exists(pfxPath))
        {
            return;
        }

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

    [LoggerMessage(EventId = 1124, Level = LogLevel.Error, Message = "Failed to persist certificate to disk — certificate is in memory but will be lost on restart")]
    private static partial void LogDiskWriteFailed(ILogger logger, Exception ex);
}
