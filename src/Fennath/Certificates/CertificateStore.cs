using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Stores certificates in memory and persists them to disk as PFX files.
/// Thread-safe for concurrent reads during TLS handshakes.
/// </summary>
public sealed partial class CertificateStore : IDisposable
{
    private readonly ConcurrentDictionary<string, X509Certificate2> _certs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _storagePath;
    private readonly ILogger<CertificateStore> _logger;

    public CertificateStore(IOptions<FennathConfig> options, ILogger<CertificateStore> logger)
    {
        _storagePath = options.Value.Certificates.StoragePath;
        _logger = logger;

        Directory.CreateDirectory(_storagePath);
        LoadFromDisk();
    }

    /// <summary>
    /// Gets a certificate by hostname (e.g., "*.example.com" or "grafana.example.com").
    /// </summary>
    public X509Certificate2? GetCertificate(string hostname)
    {
        // Try exact match first
        if (_certs.TryGetValue(hostname, out var cert))
            return cert;

        // Try wildcard match (strip first label, look up *.rest)
        var dotIndex = hostname.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex > 0)
        {
            var wildcard = "*" + hostname[dotIndex..];
            if (_certs.TryGetValue(wildcard, out cert))
                return cert;
        }

        return null;
    }

    /// <summary>
    /// Stores a certificate for the given hostname, both in memory and on disk.
    /// </summary>
    public void StoreCertificate(string hostname, X509Certificate2 certificate)
    {
        var old = _certs.TryGetValue(hostname, out var existing) ? existing : null;

        _certs[hostname] = certificate;
        SaveToDisk(hostname, certificate);

        old?.Dispose();
        LogCertificateStored(_logger, hostname, certificate.NotAfter);
    }

    /// <summary>
    /// Gets all stored certificates and their expiry dates.
    /// </summary>
    public IReadOnlyDictionary<string, DateTime> GetCertificateExpiries()
    {
        return _certs.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.NotAfter,
            StringComparer.OrdinalIgnoreCase);
    }

    private void LoadFromDisk()
    {
        foreach (var file in Directory.EnumerateFiles(_storagePath, "*.pfx"))
        {
            try
            {
                var cert = X509CertificateLoader.LoadPkcs12FromFile(file, null,
                    X509KeyStorageFlags.Exportable);
                var hostname = Path.GetFileNameWithoutExtension(file).Replace("_", ".");

                if (cert.NotAfter > DateTime.UtcNow)
                {
                    _certs[hostname] = cert;
                    LogCertificateLoaded(_logger, hostname, cert.NotAfter);
                }
                else
                {
                    LogCertificateExpired(_logger, hostname, cert.NotAfter);
                    cert.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogCertificateLoadFailed(_logger, file, ex);
            }
        }
    }

    private void SaveToDisk(string hostname, X509Certificate2 certificate)
    {
        var fileName = hostname.Replace(".", "_").Replace("*", "wildcard") + ".pfx";
        var path = Path.Combine(_storagePath, fileName);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
    }

    public void Dispose()
    {
        foreach (var cert in _certs.Values)
        {
            cert.Dispose();
        }

        _certs.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Certificate stored for {hostname}, expires {expiry}")]
    private static partial void LogCertificateStored(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded certificate for {hostname} from disk, expires {expiry}")]
    private static partial void LogCertificateLoaded(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped expired certificate for {hostname} (expired {expiry})")]
    private static partial void LogCertificateExpired(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load certificate from {path}")]
    private static partial void LogCertificateLoadFailed(ILogger logger, string path, Exception ex);
}
