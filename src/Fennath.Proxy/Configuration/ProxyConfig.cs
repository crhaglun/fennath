using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Fennath.Proxy.Configuration;

/// <summary>
/// Configuration for the proxy container. Bound from the "Fennath" section.
/// Only declares fields the proxy needs — DNS credentials, ACME settings,
/// and Docker config belong to the operator.
/// </summary>
public sealed class ProxyConfig
{
    public const string SectionName = "Fennath";

    [Required]
    public string Domain { get; set; } = "";

    public string Subdomain { get; set; } = "";

    public string EffectiveDomain =>
        string.IsNullOrEmpty(Subdomain) ? Domain : $"{Subdomain}.{Domain}";

    [ValidateObjectMembers]
    public ProxyServerConfig Server { get; set; } = new();

    public ProxyCertificateConfig Certificates { get; set; } = new();

    /// <summary>
    /// Path to the YARP proxy config JSON file on the shared volume,
    /// written by the operator and watched by the proxy for hot-reload.
    /// </summary>
    public string YarpConfigPath { get; set; } = "/data/shared/yarp-config.json";
}

public sealed class ProxyServerConfig
{
    [Range(1, 65535)]
    public int HttpsPort { get; set; } = 443;

    [Range(1, 65535)]
    public int HttpPort { get; set; } = 80;

    /// <summary>
    /// The external-facing HTTPS port for redirect URLs. When the container listens
    /// on a non-standard port (e.g. 8443) behind port-forwarding, this ensures
    /// HTTP→HTTPS redirects point to the correct external port.
    /// </summary>
    [Range(1, 65535)]
    public int ExternalHttpsPort { get; set; } = 443;
}

public sealed class ProxyCertificateConfig
{
    public string StoragePath { get; set; } = "/data/certs";
}

[OptionsValidator]
internal partial class ValidateProxyConfig : IValidateOptions<ProxyConfig>;

