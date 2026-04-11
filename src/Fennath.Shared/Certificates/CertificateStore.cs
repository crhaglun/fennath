using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fennath.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fennath.Certificates;

/// <summary>
/// Reads TLS certificates from disk and provides SNI-based selection with memoization.
/// Supports multiple certificates from multiple operators (multi-domain deployments).
/// Thread-safe for concurrent reads during TLS handshakes.
///
/// <para>Certificate selection order for a given hostname:</para>
/// <list type="number">
///   <item>Memoized lookup cache (populated on first hit per hostname)</item>
///   <item>Exact hostname match (e.g., <c>grafana.lab.example.com</c>)</item>
///   <item>Wildcard match (e.g., <c>*.lab.example.com</c>)</item>
///   <item>Configured domain cert (for null-SNI or unknown hostnames)</item>
///   <item>Self-signed fallback (guarantees non-null return)</item>
/// </list>
/// </summary>
public sealed partial class CertificateStore : IDisposable
{
    private readonly Lock _lock = new();
    private readonly string _storagePath;
    private readonly ILogger<CertificateStore> _logger;
    private readonly X509Certificate2 _fallbackCert;

    /// <summary>
    /// Immutable snapshot of loaded certificates and their lookup cache.
    /// Swapped atomically on reload so readers always see a consistent view.
    /// </summary>
    private sealed record CertificateSnapshot(
        Dictionary<string, X509Certificate2> Certificates,
        ConcurrentDictionary<string, X509Certificate2> LookupCache);

    private volatile CertificateSnapshot _snapshot;

    public CertificateStore(IOptions<CertificateStoreOptions> options, ILogger<CertificateStore> logger)
    {
        _storagePath = options.Value.StoragePath;
        _logger = logger;
        _fallbackCert = GenerateFallbackCert();
        _snapshot = BuildSnapshot();
    }

    /// <summary>
    /// True when no real certificates have been loaded from disk.
    /// </summary>
    public bool IsPlaceholder => _snapshot.Certificates.Count == 0;

    /// <summary>
    /// Returns a certificate for the given hostname via memoized SNI selection.
    /// Never returns null — falls back to a self-signed placeholder.
    /// </summary>
    public X509Certificate2 GetCertificate(string? hostname)
    {
        var snapshot = _snapshot;

        // Fast path: memoized lookup
        if (hostname is not null && snapshot.LookupCache.TryGetValue(hostname, out var cached))
        {
            return cached;
        }

        // Try exact/wildcard match from the cert index
        if (hostname is not null)
        {
            var matched = ResolveFromIndex(hostname, snapshot.Certificates);
            if (matched is not null)
            {
                snapshot.LookupCache.TryAdd(hostname, matched);
                return matched;
            }
        }

        return _fallbackCert;
    }

    /// <summary>
    /// Returns the certificate storage root directory.
    /// </summary>
    public string GetStoragePath() => _storagePath;

    /// <summary>
    /// Rebuilds the certificate index from disk. Called by the file watcher
    /// when operators write new certificates to the shared volume.
    /// </summary>
    public void ReloadFromDisk()
    {
        lock (_lock)
        {
            _snapshot = BuildSnapshot();
        }
    }

    /// <summary>
    /// Scans the storage directory recursively for PFX files and builds
    /// an immutable snapshot with a fresh (empty) lookup cache.
    /// </summary>
    private CertificateSnapshot BuildSnapshot()
    {
        var certs = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(_storagePath))
        {
            foreach (var pfxFile in Directory.EnumerateFiles(_storagePath, "*.pfx", SearchOption.AllDirectories))
            {
                try
                {
                    var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxFile, null,
                        X509KeyStorageFlags.Exportable);

                    if (cert.NotAfter <= DateTime.UtcNow)
                    {
                        var expiredName = GetCertDisplayName(cert);
                        LogCertificateExpired(_logger, expiredName, cert.NotAfter);
                        cert.Dispose();
                        continue;
                    }

                    IndexCertificateInto(cert, certs);
                    var displayName = GetCertDisplayName(cert);
                    LogCertificateLoaded(_logger, displayName, cert.NotAfter);
                }
                catch (Exception ex)
                {
                    LogCertificateLoadFailed(_logger, pfxFile, ex);
                }
            }
        }

        LogCertificatesIndexed(_logger, certs.Count);

        return new CertificateSnapshot(
            certs,
            new ConcurrentDictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase));
    }

    private static X509Certificate2? ResolveFromIndex(
        string hostname, Dictionary<string, X509Certificate2> certs)
    {
        if (certs.TryGetValue(hostname, out var exact))
        {
            return exact;
        }

        var dotIndex = hostname.IndexOf('.');
        if (dotIndex > 0)
        {
            var wildcard = $"*{hostname[dotIndex..]}";
            if (certs.TryGetValue(wildcard, out var wild))
            {
                return wild;
            }
        }

        return null;
    }

    private static void IndexCertificateInto(
        X509Certificate2 cert, Dictionary<string, X509Certificate2> certs)
    {
        foreach (var hostname in GetCertificateHostnames(cert))
        {
            // Last-loaded cert wins — a shorter validity may be a legitimate
            // provider change, and we don't dispose the old cert because it
            // could still be in use by an in-flight TLS handshake.
            certs[hostname] = cert;
        }
    }

    /// <summary>
    /// Extracts hostnames from a certificate's SANs and CN for index keying.
    /// </summary>
    internal static IEnumerable<string> GetCertificateHostnames(X509Certificate2 cert)
    {
        var found = false;

        foreach (var ext in cert.Extensions)
        {
            if (ext is X509SubjectAlternativeNameExtension san)
            {
                foreach (var dns in san.EnumerateDnsNames())
                {
                    found = true;
                    yield return dns;
                }
            }
        }

        // Fall back to CN if no SANs
        if (!found)
        {
            var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
            if (!string.IsNullOrEmpty(cn))
            {
                yield return cn;
            }
        }
    }

    private static string GetCertDisplayName(X509Certificate2 cert)
    {
        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        return string.IsNullOrEmpty(cn) ? cert.Thumbprint[..8] : cn;
    }

    private static X509Certificate2 GenerateFallbackCert()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=placeholder", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        var pfxBytes = cert.Export(X509ContentType.Pfx);
        cert.Dispose();
        return X509CertificateLoader.LoadPkcs12(pfxBytes, null,
            X509KeyStorageFlags.Exportable);
    }

    public void Dispose()
    {
        _fallbackCert.Dispose();
        foreach (var cert in _snapshot.Certificates.Values.Distinct())
        {
            cert.Dispose();
        }
    }

    [LoggerMessage(EventId = 1121, Level = LogLevel.Information, Message = "Loaded certificate for {hostname} from disk, expires {expiry}")]
    private static partial void LogCertificateLoaded(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(EventId = 1122, Level = LogLevel.Warning, Message = "Skipped expired certificate for {hostname} (expired {expiry})")]
    private static partial void LogCertificateExpired(ILogger logger, string hostname, DateTime expiry);

    [LoggerMessage(EventId = 1123, Level = LogLevel.Warning, Message = "Failed to load certificate from {path}")]
    private static partial void LogCertificateLoadFailed(ILogger logger, string path, Exception ex);

    [LoggerMessage(EventId = 1125, Level = LogLevel.Information, Message = "Certificate store indexed {count} hostname entries")]
    private static partial void LogCertificatesIndexed(ILogger logger, int count);
}
