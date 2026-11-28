using FluentValidation;

namespace GroundUp.Auth.Core.Validators;

/// <summary>
/// Shared rules for a list of permission keys given to a policy (GU-96). Whether each key exists and
/// may be used by a tenant is checked by the policy service against the catalogue.
/// </summary>
internal static class PermissionKeyListRules
{
    /// <summary>The most keys one request may give.</summary>
    public const int MaxKeys = 200;

    /// <summary>The longest key (matches the permission key column).</summary>
    public const int MaxKeyLength = 200;

    /// <summary>
    /// At most <see cref="MaxKeys"/> keys, each non-empty and at most <see cref="MaxKeyLength"/> characters.
    /// </summary>
    public static void Apply<T>(IRuleBuilder<T, List<string>?> rule)
    {
        rule.Must(keys => keys is null || keys.Count <= MaxKeys)
            .WithMessage($"At most {MaxKeys} permission keys can be given.");
        rule.ForEach(key => key
            .NotEmpty().WithMessage("A permission key cannot be empty.")
            .MaximumLength(MaxKeyLength));
    }
}
