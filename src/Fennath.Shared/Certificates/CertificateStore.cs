using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fennath.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Stores the single wildcard certificate in memory and on disk.
/// Thread-safe for concurrent reads during TLS handshakes.
/// Supports file-watching reload for operator architecture (proxy watches
/// cert files written by the operator).
/// </summary>
/// <remarks>
/// The <c>_certificate</c> field is never null — on construction it is initialized
/// to either a cert loaded from disk or a self-signed placeholder. This guarantees
/// that <see cref="GetCertificate"/> always returns a usable certificate for Kestrel's
/// <c>ServerCertificateSelector</c>, and eliminates null windows during reload.
/// </remarks>
public sealed partial class CertificateStore : IDisposable
{
    private readonly Lock _lock = new();
    private readonly string _storagePath;
    private readonly string _wildcardHost;
    private readonly ILogger<CertificateStore> _logger;

    private X509Certificate2 _certificate;
    private bool _isPlaceholder;

    public CertificateStore(IOptions<FennathConfig> options, ILogger<CertificateStore> logger)
    {
        _storagePath = options.Value.Certificates.StoragePath;
        _wildcardHost = $"*.{options.Value.Domain}";
        _logger = logger;

        Directory.CreateDirectory(_storagePath);

        if (TryLoadFromDisk(out var cert))
        {
            _certificate = cert;
            _isPlaceholder = false;
        }
        else
        {
            _certificate = GeneratePlaceholderCert();
            _isPlaceholder = true;
        }
    }

    /// <summary>
    /// True when the store holds a self-signed placeholder certificate because
    /// no real certificate has been loaded from disk or provisioned yet.
    /// </summary>
    public bool IsPlaceholder => _isPlaceholder;

    /// <summary>
    /// Returns the current certificate. Never null — returns a self-signed
    /// placeholder when no real certificate is available.
    /// </summary>
    public X509Certificate2 GetCertificate() => _certificate;

    /// <summary>
    /// Stores a certificate from Let's Encrypt, replacing any existing one.
    /// </summary>
    public void StoreCertificate(X509Certificate2 certificate)
    {
        lock (_lock)
        {
            var old = _certificate;
            _certificate = certificate;
            _isPlaceholder = false;

            try
            {
                SaveToDisk(certificate);
            }
            catch (Exception ex)
            {
                LogDiskWriteFailed(_logger, ex);
            }

            if (old != certificate)
            {
                old.Dispose();
            }
        }

        LogCertificateStored(_logger, _wildcardHost, certificate.NotAfter);
    }

    /// <summary>
    /// Returns the expiry of the current certificate.
    /// </summary>
    public DateTime GetExpiry() => _certificate.NotAfter;

    /// <summary>
    /// Reloads the certificate from disk. Called by the file watcher when
    /// the operator writes a new certificate to the shared volume.
    /// Uses load-then-swap to avoid any null window visible to concurrent readers.
    /// </summary>
    public void ReloadFromDisk()
    {
        lock (_lock)
        {
            if (TryLoadFromDisk(out var loaded))
            {
                var old = _certificate;
                _certificate = loaded;
                _isPlaceholder = false;

                if (old != loaded)
                {
                    old.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Returns the path where the wildcard certificate PFX is stored.
    /// </summary>
    public string GetCertificatePath() => Path.Combine(_storagePath, "wildcard.pfx");

    private bool TryLoadFromDisk(out X509Certificate2 certificate)
    {
        certificate = null!;
        var pfxPath = Path.Combine(_storagePath, "wildcard.pfx");
        if (!File.Exists(pfxPath))
        {
            return false;
        }

        try
        {
            var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, null,
                X509KeyStorageFlags.Exportable);

            if (cert.NotAfter > DateTime.UtcNow)
            {
                LogCertificateLoaded(_logger, _wildcardHost, cert.NotAfter);
                certificate = cert;
                return true;
            }

            LogCertificateExpired(_logger, _wildcardHost, cert.NotAfter);
            cert.Dispose();
            return false;
        }
        catch (Exception ex)
        {
            LogCertificateLoadFailed(_logger, pfxPath, ex);
            return false;
        }
    }

    private X509Certificate2 GeneratePlaceholderCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={_wildcardHost}", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(1);

        var cert = request.CreateSelfSigned(notBefore, notAfter);

        // Re-import so the private key is usable across platforms
        var pfxBytes = cert.Export(X509ContentType.Pfx);
        cert.Dispose();
        return X509CertificateLoader.LoadPkcs12(pfxBytes, null,
            X509KeyStorageFlags.Exportable);
    }

    private void SaveToDisk(X509Certificate2 certificate)
    {
        var path = Path.Combine(_storagePath, "wildcard.pfx");
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, certificate.Export(X509ContentType.Pfx));
        File.Move(tempPath, path, overwrite: true);
    }

    public void Dispose()
    {
        _certificate.Dispose();
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
