using GroundUp.Core.Configuration;
using Microsoft.Extensions.Options;

namespace GroundUp.Services.Configuration;

/// <summary>
/// Validates BootstrapOptions at startup. Fails fast on missing required values.
/// The BootstrapAdminToken required-when-incomplete check lives in BootstrapTokenStartupValidator
/// (which has DB access); this validator only checks the length constraint.
/// </summary>
public sealed class BootstrapOptionsValidator : IValidateOptions<BootstrapOptions>
{
    public ValidateOptionsResult Validate(string? name, BootstrapOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DatabaseConnection))
            failures.Add("GroundUp:DatabaseConnection is required.");

        if (string.IsNullOrWhiteSpace(options.MasterKey) &&
            string.IsNullOrWhiteSpace(options.MasterKeyPath))
            failures.Add("Either GroundUp:MasterKey or GroundUp:MasterKeyPath must be configured.");

        if (!string.IsNullOrEmpty(options.BootstrapAdminToken) &&
            options.BootstrapAdminToken.Length < 32)
            failures.Add("GroundUp:BootstrapAdminToken must be at least 32 characters.");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
