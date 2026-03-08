using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Fennath.Configuration;

/// <summary>
/// Root configuration model, bound from the "Fennath" configuration section.
/// </summary>
public sealed class FennathConfig
{
    public const string SectionName = "Fennath";

    [Required]
    public required string Domain { get; set; }
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

    [Range(1, int.MaxValue)]
    public int DnsPropagationWaitSeconds { get; set; } = 30;

    [Range(1, int.MaxValue)]
    public int ChallengePollingIntervalSeconds { get; set; } = 120;
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
