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
    public List<RouteEntry> Routes { get; set; } = [];
    public DockerConfig Docker { get; set; } = new();
    public TelemetryConfig Telemetry { get; set; } = new();
    public ServerConfig Server { get; set; } = new();
}

public sealed class DnsConfig
{
    public string Provider { get; set; } = "loopia";
    public LoopiaConfig Loopia { get; set; } = new();
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
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class CertificateConfig
{
    public string Email { get; set; } = "";
    public bool Wildcard { get; set; } = true;
    public bool Staging { get; set; }
    public string StoragePath { get; set; } = "/data/certs";
}

public sealed class RouteEntry
{
    [Required]
    public required string Subdomain { get; set; }

    [Required, Url]
    public required string Backend { get; set; }

    public HealthCheckEntry? HealthCheck { get; set; }
    public RouteCertificateConfig? Certificate { get; set; }
}

public sealed class HealthCheckEntry
{
    public string Path { get; set; } = "/";
    public int IntervalSeconds { get; set; } = 30;
}

public sealed class RouteCertificateConfig
{
    public string Mode { get; set; } = "wildcard";
}

public sealed class DockerConfig
{
    public bool Enabled { get; set; }
    public string SocketPath { get; set; } = "/var/run/docker.sock";
}

public sealed class TelemetryConfig
{
    public string? Endpoint { get; set; }
    public string Protocol { get; set; } = "grpc";
    public Dictionary<string, string> Headers { get; set; } = [];
    public string ServiceName { get; set; } = "fennath";
}

public sealed class ServerConfig
{
    public int HttpsPort { get; set; } = 443;
    public int HttpPort { get; set; } = 80;
    public bool HttpToHttpsRedirect { get; set; } = true;
}
