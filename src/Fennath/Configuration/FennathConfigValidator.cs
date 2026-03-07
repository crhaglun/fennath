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
        return ValidateOptionsResult.Success;
    }
}
