using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Fennath.Operator.Configuration;

/// <summary>
/// Configuration for the operator container. Bound from the "Fennath" section.
/// Only declares fields the operator needs — server/port config belongs to the proxy.
/// </summary>
public sealed class OperatorConfig
{
    public const string SectionName = "Fennath";

    [Required]
    public string Domain { get; set; } = "";

    public string Subdomain { get; set; } = "";

    public string EffectiveDomain =>
        string.IsNullOrEmpty(Subdomain) ? Domain : $"{Subdomain}.{Domain}";

    [ValidateObjectMembers]
    public DnsConfig Dns { get; set; } = new();

    [ValidateObjectMembers]
    public CertificateConfig Certificates { get; set; } = new();

    [ValidateObjectMembers]
    public DockerConfig Docker { get; set; } = new();
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
    public int DnsPropagationTimeoutSeconds { get; set; } = 86400;

    [Range(1, int.MaxValue)]
    public int DnsPropagationPollingIntervalSeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int ChallengePollingIntervalSeconds { get; set; } = 120;

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

[OptionsValidator]
internal partial class ValidateOperatorConfig : IValidateOptions<OperatorConfig>;
