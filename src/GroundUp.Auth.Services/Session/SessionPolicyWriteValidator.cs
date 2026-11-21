using System.Globalization;
using GroundUp.Auth.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Auth.Services.Session;

/// <summary>
/// Cross-value rules for the session timeout settings, checked when a value is written:
/// a tenant value may not exceed the System value, and the absolute lifetime may not be shorter
/// than the token lifetime. Settings of other keys are ignored.
/// <para>
/// The session policy provider is resolved on demand, not injected, because the settings
/// service receives this validator and the provider depends on the settings service.
/// </para>
/// </summary>
public sealed class SessionPolicyWriteValidator : ISettingWriteValidator
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="SessionPolicyWriteValidator"/>.
    /// </summary>
    /// <param name="serviceProvider">The scoped provider, used to resolve <see cref="ISessionPolicyProvider"/> on demand.</param>
    public SessionPolicyWriteValidator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task<string?> ValidateAsync(
        SettingWriteContext context,
        CancellationToken cancellationToken = default)
    {
        var isTokenKey = context.Key == SessionSettingKeys.TokenLifetimeMinutes;
        var isAbsoluteKey = context.Key == SessionSettingKeys.AbsoluteLifetimeMinutes;
        if (!isTokenKey && !isAbsoluteKey)
        {
            return null;
        }

        // Clearing a value is allowed: the level then inherits from its parent.
        if (string.IsNullOrWhiteSpace(context.Value))
        {
            return null;
        }

        if (!int.TryParse(context.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            return $"Setting '{context.Key}' must be a whole number of minutes.";
        }

        var isTenant = string.Equals(context.LevelName, "Tenant", StringComparison.OrdinalIgnoreCase);
        var isSystem = string.Equals(context.LevelName, "System", StringComparison.OrdinalIgnoreCase);
        if (!isTenant && !isSystem)
        {
            return null;
        }

        var provider = _serviceProvider.GetRequiredService<ISessionPolicyProvider>();
        SessionPolicy policy;

        if (isTenant)
        {
            if (context.ScopeId is null)
            {
                return $"A tenant value for '{context.Key}' must specify the tenant.";
            }

            var system = await provider.GetSystemPolicyAsync(cancellationToken);
            var systemMinutes = (int)(isTokenKey ? system.TokenLifetime : system.AbsoluteLifetime).TotalMinutes;
            if (minutes > systemMinutes)
            {
                return $"A tenant value for '{context.Key}' cannot be longer than the System value ({systemMinutes} minutes).";
            }

            policy = await provider.GetPolicyForTenantAsync(context.ScopeId.Value, cancellationToken);
        }
        else
        {
            policy = await provider.GetSystemPolicyAsync(cancellationToken);
        }

        if (isTokenKey)
        {
            var absoluteMinutes = (int)policy.AbsoluteLifetime.TotalMinutes;
            if (minutes > absoluteMinutes)
            {
                return $"The token lifetime ({minutes} minutes) cannot be longer than the absolute session lifetime ({absoluteMinutes} minutes).";
            }
        }
        else
        {
            var tokenMinutes = (int)policy.TokenLifetime.TotalMinutes;
            if (minutes < tokenMinutes)
            {
                return $"The absolute session lifetime ({minutes} minutes) cannot be shorter than the token lifetime ({tokenMinutes} minutes).";
            }
        }

        return null;
    }
}
