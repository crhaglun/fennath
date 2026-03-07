using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fennath.Certificates;
using Fennath.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fennath.Tests.Unit;

public class CertificateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CertificateStore _store;

    public CertificateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fennath-cert-test-{Guid.NewGuid():N}");
        var options = Options.Create(new FennathConfig
        {
            Domain = "example.com",
            Dns = new DnsConfig { Provider = "loopia", Loopia = new LoopiaConfig { Username = "u", Password = "p" } },
            Certificates = new CertificateConfig
            {
                Email = "test@example.com",
                StoragePath = _tempDir
            },
            Routes = []
        });

        _store = new CertificateStore(options, NullLogger<CertificateStore>.Instance);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task GetCertificate_ExactMatch_ReturnsCertificate()
    {
        using var cert = CreateSelfSignedCert("grafana.example.com");
        _store.StoreCertificate("grafana.example.com", cert);

        var result = _store.GetCertificate("grafana.example.com");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Subject).Contains("grafana.example.com");
    }

    [Test]
    public async Task GetCertificate_WildcardMatch_ReturnsCertificate()
    {
        using var cert = CreateSelfSignedCert("*.example.com");
        _store.StoreCertificate("*.example.com", cert);

        var result = _store.GetCertificate("grafana.example.com");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Subject).Contains("*.example.com");
    }

    [Test]
    public async Task GetCertificate_NoPersistedCert_ReturnsSelfSignedFallback()
    {
        var result = _store.GetCertificate("unknown.example.com");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Subject).Contains("unknown.example.com");
        await Assert.That(_store.IsSelfSigned("unknown.example.com")).IsTrue();
    }

    [Test]
    public async Task GetCertificate_ExactMatchTakesPrecedenceOverWildcard()
    {
        using var wildcardCert = CreateSelfSignedCert("*.example.com");
        using var exactCert = CreateSelfSignedCert("grafana.example.com");

        _store.StoreCertificate("*.example.com", wildcardCert);
        _store.StoreCertificate("grafana.example.com", exactCert);

        var result = _store.GetCertificate("grafana.example.com");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Subject).Contains("grafana.example.com");
    }

    [Test]
    public async Task StoreCertificate_PersistsToDisk()
    {
        using var cert = CreateSelfSignedCert("test.example.com");
        _store.StoreCertificate("test.example.com", cert);

        var pfxFiles = Directory.GetFiles(_tempDir, "*.pfx");

        await Assert.That(pfxFiles).Count().IsEqualTo(1);
        await Assert.That(pfxFiles[0]).Contains("test_example_com");
    }

    [Test]
    public async Task LoadFromDisk_LoadsValidCertificates()
    {
        // Create and persist a cert via first store instance
        using var cert = CreateSelfSignedCert("persisted.example.com");
        _store.StoreCertificate("persisted.example.com", cert);

        // Create a new store instance that should load from disk
        var options = Options.Create(new FennathConfig
        {
            Domain = "example.com",
            Dns = new DnsConfig { Provider = "loopia", Loopia = new LoopiaConfig { Username = "u", Password = "p" } },
            Certificates = new CertificateConfig
            {
                Email = "test@example.com",
                StoragePath = _tempDir
            },
            Routes = []
        });

        using var newStore = new CertificateStore(options, NullLogger<CertificateStore>.Instance);
        var result = newStore.GetCertificate("persisted.example.com");

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task LoadFromDisk_SkipsExpiredCertificates()
    {
        // Write a PFX with an already-expired cert directly to disk
        using var expired = CreateSelfSignedCert("expired.example.com", TimeSpan.FromDays(-1));
        var fileName = "expired_example_com.pfx";
        await File.WriteAllBytesAsync(
            Path.Combine(_tempDir, fileName),
            expired.Export(X509ContentType.Pfx));

        // Create a new store that loads from disk
        var options = Options.Create(new FennathConfig
        {
            Domain = "example.com",
            Dns = new DnsConfig { Provider = "loopia", Loopia = new LoopiaConfig { Username = "u", Password = "p" } },
            Certificates = new CertificateConfig
            {
                Email = "test@example.com",
                StoragePath = _tempDir
            },
            Routes = []
        });

        using var newStore = new CertificateStore(options, NullLogger<CertificateStore>.Instance);
        var result = newStore.GetCertificate("expired.example.com");

        // The expired cert was not loaded — any cert returned is a self-signed fallback
        await Assert.That(newStore.IsSelfSigned("expired.example.com")).IsTrue();
        await Assert.That(newStore.GetCertificateExpiries().ContainsKey("expired.example.com")).IsFalse();
    }

    [Test]
    public async Task GetCertificateExpiries_ReturnsAllStoredCerts()
    {
        using var cert1 = CreateSelfSignedCert("a.example.com");
        using var cert2 = CreateSelfSignedCert("b.example.com");

        _store.StoreCertificate("a.example.com", cert1);
        _store.StoreCertificate("b.example.com", cert2);

        var expiries = _store.GetCertificateExpiries();

        await Assert.That(expiries).Count().IsEqualTo(2);
        await Assert.That(expiries.ContainsKey("a.example.com")).IsTrue();
        await Assert.That(expiries.ContainsKey("b.example.com")).IsTrue();
    }

    [Test]
    public async Task GetCertificate_CaseInsensitive()
    {
        using var cert = CreateSelfSignedCert("grafana.example.com");
        _store.StoreCertificate("GRAFANA.EXAMPLE.COM", cert);

        var result = _store.GetCertificate("grafana.example.com");

        await Assert.That(result).IsNotNull();
    }

    [Test]
    public async Task SelfSignedFallback_IsNotPersistedToDisk()
    {
        _ = _store.GetCertificate("ephemeral.example.com");

        var pfxFiles = Directory.GetFiles(_tempDir, "*.pfx");
        await Assert.That(pfxFiles).Count().IsEqualTo(0);
    }

    [Test]
    public async Task SelfSignedFallback_IsExcludedFromExpiries()
    {
        _ = _store.GetCertificate("ephemeral.example.com");

        var expiries = _store.GetCertificateExpiries();
        await Assert.That(expiries.ContainsKey("ephemeral.example.com")).IsFalse();
    }

    [Test]
    public async Task SelfSignedFallback_IsReplacedByRealCert()
    {
        // First call generates self-signed
        var selfSigned = _store.GetCertificate("replaced.example.com");
        await Assert.That(_store.IsSelfSigned("replaced.example.com")).IsTrue();

        // Store a real cert — replaces the self-signed
        using var realCert = CreateSelfSignedCert("replaced.example.com");
        _store.StoreCertificate("replaced.example.com", realCert);

        await Assert.That(_store.IsSelfSigned("replaced.example.com")).IsFalse();

        var expiries = _store.GetCertificateExpiries();
        await Assert.That(expiries.ContainsKey("replaced.example.com")).IsTrue();
    }

    [Test]
    public async Task SelfSignedFallback_SameHostReturnsCachedCert()
    {
        var first = _store.GetCertificate("cached.example.com");
        var second = _store.GetCertificate("cached.example.com");

        await Assert.That(first).IsSameReferenceAs(second);
    }

    private static X509Certificate2 CreateSelfSignedCert(string cn, TimeSpan? validity = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var effectiveValidity = validity ?? TimeSpan.FromDays(90);

        DateTimeOffset notBefore;
        DateTimeOffset notAfter;
        if (effectiveValidity < TimeSpan.Zero)
        {
            // Create an already-expired certificate
            notBefore = DateTimeOffset.UtcNow.AddDays(-30);
            notAfter = DateTimeOffset.UtcNow.Add(effectiveValidity);
        }
        else
        {
            notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
            notAfter = DateTimeOffset.UtcNow.Add(effectiveValidity);
        }

        var cert = request.CreateSelfSigned(notBefore, notAfter);

        // Export and reimport to get a cert with an exportable private key
        var pfxBytes = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, null,
            X509KeyStorageFlags.Exportable);
    }
}
