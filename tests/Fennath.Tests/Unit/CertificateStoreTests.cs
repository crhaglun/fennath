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
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task GetCertificate_ReturnsPlaceholderWhenNoCertOnDisk()
    {
        await Assert.That(_store.GetCertificate()).IsNotNull();
        await Assert.That(_store.IsPlaceholder).IsTrue();
    }

    [Test]
    public async Task GetCertificate_ReturnsCertAfterStore()
    {
        StoreTestCert();
        await Assert.That(_store.GetCertificate()).IsNotNull();
        await Assert.That(_store.IsPlaceholder).IsFalse();
    }

    [Test]
    public async Task IsPlaceholder_TrueWhenNoCertOnDisk()
    {
        await Assert.That(_store.IsPlaceholder).IsTrue();
    }

    [Test]
    public async Task IsPlaceholder_FalseAfterStoreCertificate()
    {
        StoreTestCert();
        await Assert.That(_store.IsPlaceholder).IsFalse();
    }

    [Test]
    public async Task IsPlaceholder_FalseAfterReloadFromDisk()
    {
        StoreTestCert();

        using var newStore = CreateStore();
        await Assert.That(newStore.IsPlaceholder).IsFalse();
    }

    [Test]
    public async Task StoreCertificate_PersistsToDisk()
    {
        StoreTestCert();

        var pfxFiles = Directory.GetFiles(_tempDir, "*.pfx");
        await Assert.That(pfxFiles).Count().IsEqualTo(1);
    }

    [Test]
    public async Task LoadFromDisk_LoadsValidCertificate()
    {
        StoreTestCert();

        using var newStore = CreateStore();
        var remaining = newStore.GetExpiry() - DateTime.UtcNow;
        await Assert.That(remaining.TotalDays).IsGreaterThan(80);
    }

    [Test]
    public async Task LoadFromDisk_SkipsExpiredCertificate()
    {
        Directory.CreateDirectory(_tempDir);
        using var expired = CreateCert(TimeSpan.FromDays(-1));
        await File.WriteAllBytesAsync(
            Path.Combine(_tempDir, "wildcard.pfx"),
            expired.Export(X509ContentType.Pfx));

        using var newStore = CreateStore();
        // Expired cert is skipped; store falls back to placeholder
        await Assert.That(newStore.IsPlaceholder).IsTrue();
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

    [Test]
    public async Task ReloadFromDisk_NeverReturnsNull()
    {
        // Even when there's nothing on disk, reload should keep the placeholder
        _store.ReloadFromDisk();
        await Assert.That(_store.GetCertificate()).IsNotNull();
        await Assert.That(_store.IsPlaceholder).IsTrue();
    }

    private void StoreTestCert()
    {
        var cert = CreateCert();
        _store.StoreCertificate(cert);
    }

    private CertificateStore CreateStore()
    {
        var options = Options.Create(new CertificateStoreOptions
        {
            Domain = "example.com",
            StoragePath = _tempDir
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
