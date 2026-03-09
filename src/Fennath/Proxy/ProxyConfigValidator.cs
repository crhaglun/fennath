using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Proxy;

/// <summary>
/// Validates the configuration subset required by the proxy container.
/// Does NOT require DNS credentials (those belong to the sidecar).
/// </summary>
public sealed class ProxyConfigValidator : IValidateOptions<FennathConfig>
{
    public ValidateOptionsResult Validate(string? name, FennathConfig options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Domain))
        {
            failures.Add("Domain is required.");
        }

        if (options.Server.HttpsPort is < 1 or > 65535)
        {
            failures.Add("Server.HttpsPort must be between 1 and 65535.");
        }

        if (options.Server.HttpPort is < 1 or > 65535)
        {
            failures.Add("Server.HttpPort must be between 1 and 65535.");
        }

        if (options.Server.ExternalHttpsPort is < 1 or > 65535)
        {
            failures.Add("Server.ExternalHttpsPort must be between 1 and 65535.");
        }

        if (options.Docker.PollIntervalSeconds < 1)
        {
            failures.Add("Docker.PollIntervalSeconds must be at least 1.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
