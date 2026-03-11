namespace Fennath.Configuration;

/// <summary>
/// Minimal options for <see cref="Fennath.Certificates.CertificateStore"/>.
/// Each container binds this from its own configuration.
/// </summary>
public sealed class CertificateStoreOptions
{
    public string Domain { get; set; } = "";

    public string Subdomain { get; set; } = "";

    public string EffectiveDomain =>
        string.IsNullOrEmpty(Subdomain) ? Domain : $"{Subdomain}.{Domain}";

    public string StoragePath { get; set; } = "/data/certs";
}
