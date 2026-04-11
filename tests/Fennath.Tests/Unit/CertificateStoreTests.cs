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

    public CertificateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fennath-cert-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task GetCertificate_ReturnsFallbackWhenNoCertOnDisk()
    {
        using var store = CreateStore();
        await Assert.That(store.GetCertificate(null)).IsNotNull();
        await Assert.That(store.IsPlaceholder).IsTrue();
    }

    [Test]
    public async Task GetCertificate_ReturnsCertAfterReload()
    {
        WriteCertToDisk();
        using var store = CreateStore();

        var cert = store.GetCertificate("anything.example.com");
        await Assert.That(cert).IsNotNull();
        await Assert.That(store.IsPlaceholder).IsFalse();
    }

    [Test]
    public async Task IsPlaceholder_TrueWhenNoCertOnDisk()
    {
        using var store = CreateStore();
        await Assert.That(store.IsPlaceholder).IsTrue();
    }

    [Test]
    public async Task IsPlaceholder_FalseAfterReloadWithCert()
    {
        using var store = CreateStore();
        WriteCertToDisk();
        store.ReloadFromDisk();

        await Assert.That(store.IsPlaceholder).IsFalse();
    }

    [Test]
    public async Task LoadFromDisk_LoadsValidCertificate()
    {
        WriteCertToDisk();
        using var store = CreateStore();

        var cert = store.GetCertificate("anything.example.com");
        await Assert.That(cert.NotAfter > DateTime.UtcNow).IsTrue();
    }

    [Test]
    public async Task LoadFromDisk_SkipsExpiredCertificate()
    {
        Directory.CreateDirectory(_tempDir);
        using var expired = CreateCert(TimeSpan.FromDays(-1));
        File.WriteAllBytes(
            Path.Combine(_tempDir, "example.com.pfx"),
            expired.Export(X509ContentType.Pfx));

        using var store = CreateStore();
        await Assert.That(store.IsPlaceholder).IsTrue();
    }

    [Test]
    public async Task ReloadFromDisk_NeverReturnsNull()
    {
        using var store = CreateStore();
        store.ReloadFromDisk();
        await Assert.That(store.GetCertificate(null)).IsNotNull();
        await Assert.That(store.IsPlaceholder).IsTrue();
    }

    private void WriteCertToDisk()
    {
        Directory.CreateDirectory(_tempDir);
        using var cert = CreateCert();
        File.WriteAllBytes(
            Path.Combine(_tempDir, "example.com.pfx"),
            cert.Export(X509ContentType.Pfx));
    }

    private CertificateStore CreateStore()
    {
        var options = Options.Create(new CertificateStoreOptions
        {
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
