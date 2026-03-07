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
        _store = CreateStore();
    }

    public void Dispose()
    {
        _store.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task GetCertificate_ReturnsNullWhenEmpty()
    {
        await Assert.That(_store.GetCertificate()).IsNull();
    }

    [Test]
    public async Task GetCertificate_ReturnsCertAfterStore()
    {
        StoreTestCert();
        await Assert.That(_store.GetCertificate()).IsNotNull();
    }

    [Test]
    public async Task GetExpiry_ReturnsNullWhenEmpty()
    {
        await Assert.That(_store.GetExpiry()).IsNull();
    }

    [Test]
    public async Task StoreCertificate_SetsExpiry()
    {
        StoreTestCert();
        await Assert.That(_store.GetExpiry()).IsNotNull();
    }

    [Test]
    public async Task StoreCertificate_PersistsToDisk()
    {
        StoreTestCert();

        var pfxFiles = Directory.GetFiles(_tempDir, "*.pfx");
        await Assert.That(pfxFiles).Count().IsEqualTo(1);
        await Assert.That(pfxFiles[0]).Contains("wildcard");
    }

    [Test]
    public async Task LoadFromDisk_LoadsValidCertificate()
    {
        StoreTestCert();

        using var newStore = CreateStore();
        await Assert.That(newStore.GetExpiry()).IsNotNull();
    }

    [Test]
    public async Task LoadFromDisk_SkipsExpiredCertificate()
    {
        using var expired = CreateCert(TimeSpan.FromDays(-1));
        await File.WriteAllBytesAsync(
            Path.Combine(_tempDir, "wildcard.pfx"),
            expired.Export(X509ContentType.Pfx));

        using var newStore = CreateStore();
        await Assert.That(newStore.GetExpiry()).IsNull();
    }

    [Test]
    public async Task StoreCertificate_ReplacesExisting()
    {
        var first = CreateCert(TimeSpan.FromDays(30));
        _store.StoreCertificate(first);
        var firstExpiry = _store.GetExpiry();

        var second = CreateCert(TimeSpan.FromDays(90));
        _store.StoreCertificate(second);
        var secondExpiry = _store.GetExpiry();

        await Assert.That(secondExpiry).IsNotEqualTo(firstExpiry);
    }

    private void StoreTestCert()
    {
        var cert = CreateCert();
        _store.StoreCertificate(cert);
    }

    private CertificateStore CreateStore()
    {
        var options = Options.Create(new FennathConfig
        {
            Domain = "example.com",
            Dns = new DnsConfig { Provider = "loopia", Loopia = new LoopiaConfig { Username = "u", Password = "p" } },
            Certificates = new CertificateConfig
            {
                Email = "test@example.com",
                StoragePath = _tempDir
            }
        });

        return new CertificateStore(options, NullLogger<CertificateStore>.Instance);
    }

    private static X509Certificate2 CreateCert(TimeSpan? validity = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=*.example.com", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var effectiveValidity = validity ?? TimeSpan.FromDays(90);

        DateTimeOffset notBefore, notAfter;
        if (effectiveValidity < TimeSpan.Zero)
        {
            notBefore = DateTimeOffset.UtcNow.AddDays(-30);
            notAfter = DateTimeOffset.UtcNow.Add(effectiveValidity);
        }
        else
        {
            notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
            notAfter = DateTimeOffset.UtcNow.Add(effectiveValidity);
        }

        var cert = request.CreateSelfSigned(notBefore, notAfter);
        var pfxBytes = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfxBytes, null,
            X509KeyStorageFlags.Exportable);
    }
}
