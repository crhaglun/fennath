using System.ComponentModel.DataAnnotations;

namespace Fennath.Configuration;

/// <summary>
/// Root configuration model, bound from the "Fennath" configuration section.
/// </summary>
public sealed class FennathConfig
{
    public const string SectionName = "Fennath";

    [Required]
    public required string Domain { get; set; }
    public DnsConfig Dns { get; set; } = new();
    public CertificateConfig Certificates { get; set; } = new();
    public DockerConfig Docker { get; set; } = new();
    public ServerConfig Server { get; set; } = new();
}

public sealed class DnsConfig
{
    public string Provider { get; set; } = "loopia";
    public LoopiaConfig Loopia { get; set; } = new();
    public int PublicIpCheckIntervalSeconds { get; set; } = 300;
    public int ReconciliationIntervalSeconds { get; set; } = 86400;
    public int ReconciliationPollIntervalSeconds { get; set; } = 30;
    public List<string> IpEchoServices { get; set; } =
    [
        "https://api.ipify.org",
        "https://icanhazip.com",
        "https://checkip.amazonaws.com"
    ];
}

public sealed class LoopiaConfig
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class CertificateConfig
{
    public string Email { get; set; } = "";
    public bool Staging { get; set; }
    public string StoragePath { get; set; } = "/data/certs";
    public int RenewalCheckIntervalSeconds { get; set; } = 86400;
    public int RenewalThresholdDays { get; set; } = 30;
    public int DnsPropagationWaitSeconds { get; set; } = 30;
    public int ChallengePollingIntervalSeconds { get; set; } = 120;
}

public sealed class DockerConfig
{
    public string SocketPath { get; set; } = "/var/run/docker.sock";
    public int PollIntervalSeconds { get; set; } = 15;
}

public sealed class ServerConfig
{
    public int HttpsPort { get; set; } = 443;
    public int HttpPort { get; set; } = 80;
}
