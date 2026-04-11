namespace Fennath.Configuration;

/// <summary>
/// Options for <see cref="Fennath.Certificates.CertificateStore"/>.
/// </summary>
public sealed class CertificateStoreOptions
{
    public string StoragePath { get; set; } = "/data/certs";
}
