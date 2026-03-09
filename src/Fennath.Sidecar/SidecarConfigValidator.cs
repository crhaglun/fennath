using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Sidecar;

/// <summary>
/// Validates the configuration subset required by the sidecar container.
/// Requires DNS credentials and certificate config. Does NOT require
/// Docker or Server settings (those belong to the proxy).
/// </summary>
public sealed class SidecarConfigValidator : IValidateOptions<FennathConfig>
{
    public ValidateOptionsResult Validate(string? name, FennathConfig options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Domain))
        {
            failures.Add("Domain is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Dns.Loopia.Username))
        {
            failures.Add("Dns.Loopia.Username is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Dns.Loopia.Password))
        {
            failures.Add("Dns.Loopia.Password is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Certificates.Email))
        {
            failures.Add("Certificates.Email is required.");
        }

        if (options.Dns.PublicIpCheckIntervalSeconds < 1)
        {
            failures.Add("Dns.PublicIpCheckIntervalSeconds must be at least 1.");
        }

        if (options.Certificates.RenewalCheckIntervalSeconds < 1)
        {
            failures.Add("Certificates.RenewalCheckIntervalSeconds must be at least 1.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
