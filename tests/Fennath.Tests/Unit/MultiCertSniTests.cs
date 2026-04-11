using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Fennath.Certificates;
using Fennath.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fennath.Tests.Unit;

/// <summary>
/// Tests for multi-cert SNI selection and hostname extraction in <see cref="CertificateStore"/>.
/// </summary>
public class MultiCertSniTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<IDisposable> _disposables = [];

    public MultiCertSniTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"fennath-multicert-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            d.Dispose();
        }

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task GetCertificate_with_hostname_returns_exact_match()
    {
        WriteCertToDisk("grafana.lab.example.com", "grafana.lab.example.com");
        var store = CreateStore();

        var cert = store.GetCertificate("grafana.lab.example.com");

        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        await Assert.That(cn).IsEqualTo("grafana.lab.example.com");
    }

    [Test]
    public async Task GetCertificate_with_hostname_falls_back_to_wildcard()
    {
        WriteCertToDisk("wildcard", "*.lab.example.com");
        var store = CreateStore();

        var cert = store.GetCertificate("anything.lab.example.com");

        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        await Assert.That(cn).IsEqualTo("*.lab.example.com");
    }

    [Test]
    public async Task GetCertificate_prefers_exact_over_wildcard()
    {
        WriteCertToDisk("wildcard", "*.lab.example.com");
        WriteCertToDisk("grafana.lab.example.com", "grafana.lab.example.com");
        var store = CreateStore();

        var cert = store.GetCertificate("grafana.lab.example.com");

        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        await Assert.That(cn).IsEqualTo("grafana.lab.example.com");
    }

    [Test]
    public async Task GetCertificate_returns_placeholder_for_unknown_hostname()
    {
        var store = CreateStore();

        var cert = store.GetCertificate("unknown.example.org");

        // Falls back to placeholder; just verify it returns something
        await Assert.That(cert).IsNotNull();
        await Assert.That(store.IsPlaceholder).IsTrue();
    }

    [Test]
    public async Task GetCertificate_with_null_hostname_returns_primary()
    {
        var store = CreateStore();

        var cert = store.GetCertificate(null);

        await Assert.That(cert).IsNotNull();
    }

    [Test]
    public async Task Certs_in_subdirectories_are_discovered()
    {
        var subDir = Path.Combine(_tempDir, "lab");
        Directory.CreateDirectory(subDir);
        WriteCertToDisk("wildcard", "*.lab.example.com", subDir);

        var store = CreateStore();
        var cert = store.GetCertificate("api.lab.example.com");
        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);

        await Assert.That(cn).IsEqualTo("*.lab.example.com");
    }

    [Test]
    public async Task Multiple_domain_certs_coexist()
    {
        var labDir = Path.Combine(_tempDir, "lab");
        var appsDir = Path.Combine(_tempDir, "apps");
        Directory.CreateDirectory(labDir);
        Directory.CreateDirectory(appsDir);
        WriteCertToDisk("wildcard", "*.lab.example.com", labDir);
        WriteCertToDisk("wildcard", "*.apps.example.org", appsDir);

        var store = CreateStore();

        var labCert = store.GetCertificate("grafana.lab.example.com");
        var appsCert = store.GetCertificate("cloud.apps.example.org");

        var labCn = labCert.GetNameInfo(X509NameType.SimpleName, false);
        var appsCn = appsCert.GetNameInfo(X509NameType.SimpleName, false);

        await Assert.That(labCn).IsEqualTo("*.lab.example.com");
        await Assert.That(appsCn).IsEqualTo("*.apps.example.org");
    }

    [Test]
    public async Task ReloadFromDisk_picks_up_new_certs()
    {
        var store = CreateStore();
        await Assert.That(store.IsPlaceholder).IsTrue();

        WriteCertToDisk("grafana.lab.example.com", "grafana.lab.example.com");
        store.ReloadFromDisk();

        var cert = store.GetCertificate("grafana.lab.example.com");
        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        await Assert.That(cn).IsEqualTo("grafana.lab.example.com");
    }

    [Test]
    public async Task IsPlaceholder_false_when_multi_certs_loaded()
    {
        WriteCertToDisk("grafana.lab.example.com", "grafana.lab.example.com");
        var store = CreateStore();

        await Assert.That(store.IsPlaceholder).IsFalse();
    }

    [Test]
    public async Task GetStoragePath_returns_configured_path()
    {
        var store = CreateStore();

        await Assert.That(store.GetStoragePath()).IsEqualTo(_tempDir);
    }

    // --- GetCertificateHostnames tests ---

    [Test]
    public async Task GetCertificateHostnames_extracts_CN_when_no_SANs()
    {
        using var cert = CreateCertWithCn("grafana.lab.example.com");

        var hostnames = CertificateStore.GetCertificateHostnames(cert).ToList();

        await Assert.That(hostnames).Count().IsEqualTo(1);
        await Assert.That(hostnames[0]).IsEqualTo("grafana.lab.example.com");
    }

    [Test]
    public async Task GetCertificateHostnames_extracts_SANs_over_CN()
    {
        using var cert = CreateCertWithSan("*.lab.example.com", "lab.example.com");

        var hostnames = CertificateStore.GetCertificateHostnames(cert).ToList();

        await Assert.That(hostnames).Count().IsEqualTo(2);
        await Assert.That(hostnames).Contains("*.lab.example.com");
        await Assert.That(hostnames).Contains("lab.example.com");
    }

    // --- Helpers ---

    private CertificateStore CreateStore()
    {
        var options = Options.Create(new CertificateStoreOptions
        {
            StoragePath = _tempDir
        });

        var store = new CertificateStore(options, NullLogger<CertificateStore>.Instance);
        _disposables.Add(store);
        return store;
    }

    private void WriteCertToDisk(string filename, string cn, string? directory = null)
    {
        directory ??= _tempDir;
        Directory.CreateDirectory(directory);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={cn}", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Add SAN matching the CN so GetCertificateHostnames finds it
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(cn);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(90));

        File.WriteAllBytes(
            Path.Combine(directory, $"{filename}.pfx"),
            cert.Export(X509ContentType.Pfx));
    }

    private static X509Certificate2 CreateCertWithCn(string cn)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={cn}", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var pfx = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    private static X509Certificate2 CreateCertWithSan(params string[] dnsNames)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=test", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        foreach (var name in dnsNames)
        {
            sanBuilder.AddDnsName(name);
        }
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        var pfx = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }
}
