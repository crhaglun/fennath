using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Fennath.Configuration;

/// <summary>
/// Root configuration model, bound from the "Fennath" configuration section.
/// </summary>
public sealed class FennathConfig
{
    public const string SectionName = "Fennath";

    /// <summary>
    /// The registered domain at your registrar (e.g., "my-domain-name.se").
    /// </summary>
    [Required]
    public required string Domain { get; set; }

    /// <summary>
    /// Optional subdomain prefix that scopes all Fennath services (e.g., "lab").
    /// When set, services are exposed under {service}.{Subdomain}.{Domain}.
    /// When empty, services are exposed directly under {service}.{Domain}.
    /// </summary>
    public string Subdomain { get; set; } = "";

    /// <summary>
    /// The full domain used for routing and certificates.
    /// Combines Subdomain and Domain (e.g., "lab.my-domain-name.se" or just "my-domain-name.se").
    /// </summary>
    public string EffectiveDomain =>
        string.IsNullOrEmpty(Subdomain) ? Domain : $"{Subdomain}.{Domain}";

    [ValidateObjectMembers]
    public DnsConfig Dns { get; set; } = new();

    [ValidateObjectMembers]
    public CertificateConfig Certificates { get; set; } = new();

    [ValidateObjectMembers]
    public DockerConfig Docker { get; set; } = new();

    [ValidateObjectMembers]
    public ServerConfig Server { get; set; } = new();
}

public sealed class DnsConfig
{
    public string Provider { get; set; } = "loopia";

    [ValidateObjectMembers]
    public LoopiaConfig Loopia { get; set; } = new();

    [Range(1, int.MaxValue)]
    public int PublicIpCheckIntervalSeconds { get; set; } = 300;

    public List<string> IpEchoServices { get; set; } =
    [
        "https://api.ipify.org",
        "https://icanhazip.com",
        "https://checkip.amazonaws.com"
    ];
}

public sealed class LoopiaConfig
{
    [Required]
    public string Username { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public sealed class CertificateConfig
{
    [Required]
    public string Email { get; set; } = "";

    public bool Staging { get; set; }
    public string StoragePath { get; set; } = "/data/certs";

    [Range(1, int.MaxValue)]
    public int RenewalCheckIntervalSeconds { get; set; } = 86400;

    [Range(1, 365)]
    public int RenewalThresholdDays { get; set; } = 30;

    /// <summary>
    /// Maximum time (seconds) to wait for the ACME challenge TXT record to become
    /// visible at public DNS resolvers before giving up.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DnsPropagationTimeoutSeconds { get; set; } = 86400;

    /// <summary>
    /// How often (seconds) to query public resolvers while waiting for propagation.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DnsPropagationPollingIntervalSeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int ChallengePollingIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// Public DNS resolvers used to verify TXT record propagation before
    /// triggering ACME validation. Default: Google and Cloudflare public DNS.
    /// </summary>
    public List<string> DnsResolvers { get; set; } =
    [
        "8.8.8.8",
        "1.1.1.1"
    ];
}

public sealed class DockerConfig
{
    public string SocketPath { get; set; } = "/var/run/docker.sock";

    [Range(1, int.MaxValue)]
    public int PollIntervalSeconds { get; set; } = 15;
}

public sealed class ServerConfig
{
    [Range(1, 65535)]
    public int HttpsPort { get; set; } = 443;

    [Range(1, 65535)]
    public int HttpPort { get; set; } = 80;
}

/// <summary>
/// Source-generated validator — activates DataAnnotations recursively on nested objects
/// via <see cref="ValidateObjectMembersAttribute"/>.
/// </summary>
[OptionsValidator]
internal sealed partial class FennathConfigValidator : IValidateOptions<FennathConfig>;
