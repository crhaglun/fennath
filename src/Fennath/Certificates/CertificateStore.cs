using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Concurrent;
using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Stores certificates in memory and persists them to disk as PFX files.
/// Thread-safe for concurrent reads during TLS handshakes.
/// When no persisted certificate is available, generates a temporary self-signed
/// certificate so Kestrel can complete TLS handshakes until a real certificate
/// is provisioned from Let's Encrypt.
/// </summary>
public sealed partial class CertificateStore : IDisposable
{
    private readonly ConcurrentDictionary<string, X509Certificate2> _certs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _selfSignedHosts = new(StringComparer.OrdinalIgnoreCase);
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
    /// If no persisted certificate exists, generates and caches a temporary self-signed
    /// certificate so TLS handshakes succeed while awaiting Let's Encrypt provisioning.
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

        // No real certificate — generate a temporary self-signed fallback
        return GetOrCreateSelfSigned(hostname);
    }

    /// <summary>
    /// Returns true if the certificate for the given hostname is a temporary self-signed fallback.
    /// </summary>
    public bool IsSelfSigned(string hostname) => _selfSignedHosts.ContainsKey(hostname);

    /// <summary>
    /// Stores a certificate for the given hostname, both in memory and on disk.
    /// Replaces any temporary self-signed certificate.
    /// </summary>
    public void StoreCertificate(string hostname, X509Certificate2 certificate)
    {
        var old = _certs.TryGetValue(hostname, out var existing) ? existing : null;

        _certs[hostname] = certificate;
        _selfSignedHosts.TryRemove(hostname, out _);
        SaveToDisk(hostname, certificate);

        old?.Dispose();
        LogCertificateStored(_logger, hostname, certificate.NotAfter);
    }

    /// <summary>
    /// Gets all stored certificates and their expiry dates.
    /// Excludes temporary self-signed certificates so the renewal service
    /// treats them as "no certificate" and provisions a real one.
    /// </summary>
    public IReadOnlyDictionary<string, DateTime> GetCertificateExpiries()
    {
        return _certs
            .Where(kvp => !_selfSignedHosts.ContainsKey(kvp.Key))
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.NotAfter,
                StringComparer.OrdinalIgnoreCase);
    }

    private X509Certificate2 GetOrCreateSelfSigned(string hostname)
    {
        return _certs.GetOrAdd(hostname, h =>
        {
            var cert = GenerateSelfSignedCertificate(h);
            _selfSignedHosts[h] = true;
            LogSelfSignedGenerated(_logger, h);
            return cert;
        });
    }

    private static X509Certificate2 GenerateSelfSignedCertificate(string hostname)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            $"CN={hostname}", key, HashAlgorithmName.SHA256);

        // Add Subject Alternative Name so browsers/clients accept the cert
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostname);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(7));

        // Export/reimport for exportable key storage
        var pfxBytes = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, null,
            X509KeyStorageFlags.Exportable);
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
        _selfSignedHosts.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Certificate stored for {hostname}, expires {expiry}")]
    private static partial void LogCertificateStored(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded certificate for {hostname} from disk, expires {expiry}")]
    private static partial void LogCertificateLoaded(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped expired certificate for {hostname} (expired {expiry})")]
    private static partial void LogCertificateExpired(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load certificate from {path}")]
    private static partial void LogCertificateLoadFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No certificate for {hostname} — using temporary self-signed certificate until Let's Encrypt provisioning completes")]
    private static partial void LogSelfSignedGenerated(ILogger logger, string hostname);
}
