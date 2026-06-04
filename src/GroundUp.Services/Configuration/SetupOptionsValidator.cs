using GroundUp.Core.Configuration;
using Microsoft.Extensions.Options;

namespace GroundUp.Services.Configuration;

/// <summary>
/// Validates SetupOptions at startup. Clamps MaxRequestBodyBytes to a 4096-byte floor.
/// </summary>
public sealed class SetupOptionsValidator : IValidateOptions<SetupOptions>
{
    public ValidateOptionsResult Validate(string? name, SetupOptions options)
    {
        if (options.MaxRequestBodyBytes < 4096)
            options.MaxRequestBodyBytes = 4096;

        return ValidateOptionsResult.Success;
    }
}
