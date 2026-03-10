using Fennath.Configuration;
using Microsoft.Extensions.Options;

namespace Fennath.Operator;

/// <summary>
/// Validates the configuration subset required by the operator container.
/// Requires DNS credentials, certificate config, and Docker config.
/// Does NOT require Server settings (those belong to the proxy).
/// </summary>
public sealed class OperatorConfigValidator : IValidateOptions<FennathConfig>
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

        if (options.Docker.PollIntervalSeconds < 1)
        {
            failures.Add("Docker.PollIntervalSeconds must be at least 1.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
