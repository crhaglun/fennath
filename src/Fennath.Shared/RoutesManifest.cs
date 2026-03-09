using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fennath.Core;

/// <summary>
/// Serialization model for the routes manifest file (routes.json) on the shared volume.
/// Written by the proxy (which discovers routes from Docker), read by the sidecar
/// (which creates DNS records for discovered subdomains).
/// </summary>
public sealed class RoutesManifest
{
    /// <summary>
    /// UTC timestamp of when this manifest was written.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Active subdomains discovered by the proxy. The sidecar creates DNS A records
    /// for each subdomain. Use "@" for the apex/root domain.
    /// </summary>
    [JsonPropertyName("subdomains")]
    public List<string> Subdomains { get; set; } = [];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static RoutesManifest? FromJson(string json) =>
        JsonSerializer.Deserialize<RoutesManifest>(json, SerializerOptions);
}
