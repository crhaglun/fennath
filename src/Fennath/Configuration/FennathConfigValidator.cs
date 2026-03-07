using Microsoft.Extensions.Options;

namespace Fennath.Configuration;

/// <summary>
/// Validates <see cref="FennathConfig"/> for cross-field rules that DataAnnotations can't express.
/// Field-level rules ([Required], [Url]) are on the model itself.
/// Registered via ValidateOnStart() so invalid configuration fails fast at startup.
/// </summary>
public sealed class FennathConfigValidator : IValidateOptions<FennathConfig>
{
    public ValidateOptionsResult Validate(string? name, FennathConfig config)
    {
        var errors = new List<string>();

        for (var i = 0; i < config.Routes.Count; i++)
        {
            var route = config.Routes[i];

            if (!string.IsNullOrWhiteSpace(route.Backend)
                && !Uri.TryCreate(route.Backend, UriKind.Absolute, out _))
            {
                errors.Add($"Route '{route.Subdomain}': has an invalid 'Backend' URL: '{route.Backend}'.");
            }
        }

        var duplicates = config.Routes
            .Where(r => !string.IsNullOrWhiteSpace(r.Subdomain))
            .GroupBy(r => r.Subdomain, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var dup in duplicates)
            errors.Add($"Duplicate subdomain '{dup}' in routes.");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
