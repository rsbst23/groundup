using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Auth.Services.Events;
using GroundUp.Auth.Services.Membership;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Identity;

/// <summary>
/// Finds or creates the GroundUp user for an identity-provider sign-in, claiming a pending user when
/// the sign-in proves it is theirs (GU-68 Requirements 2.4-2.6). Every sign-in flow uses it, so this
/// is the one place where pending users are claimed; GU-69 adds enterprise matches here.
/// <para>Order:</para>
/// <list type="number">
/// <item>Known identity: the user with this <c>ExternalUserId</c>. If an invitation's pending user
/// was given and is someone else, it is merged into this user.</item>
/// <item>Invitation claim: the invitation's pending user is linked to this identity.</item>
/// <item>Verified-email claim: the identity provider marks the email verified and exactly one
/// pending user has that email; it is linked.</item>
/// <item>Otherwise a new user.</item>
/// </list>
/// Linking does not activate any pending membership; only accepting the invitation does.
/// </summary>
internal sealed class SignInUserResolver
{
    private const string EmailVerifiedAttribute = "email_verified";

    private readonly IUserRepository _userRepository;
    private readonly TenantAccessRevoker _accessRevoker;
    private readonly ILogger<SignInUserResolver> _logger;

    public SignInUserResolver(
        IUserRepository userRepository,
        TenantAccessRevoker accessRevoker,
        ILogger<SignInUserResolver> logger)
    {
        _userRepository = userRepository;
        _accessRevoker = accessRevoker;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the user for <paramref name="userInfo"/>.
    /// </summary>
    /// <param name="userInfo">The signed-in identity.</param>
    /// <param name="invitedPendingUserId">
    /// The pending user of the invitation being accepted, when the sign-in comes through one.
    /// The invitation link proves the identity owns it.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The user could be neither found nor created.</exception>
    public async Task<UserDto> ResolveAsync(
        ExternalUserInfo userInfo,
        Guid? invitedPendingUserId = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Known identity
        var known = await FindByIdentityAsync(userInfo.ExternalUserId, cancellationToken);
        if (known is not null)
        {
            if (invitedPendingUserId is { } pendingId && pendingId != known.Id)
            {
                await MergeAsync(pendingId, known.Id, cancellationToken);
            }

            return known;
        }

        // 2. Invitation claim
        if (invitedPendingUserId is { } invitedId)
        {
            var linked = await TryLinkAsync(invitedId, userInfo, cancellationToken);
            if (linked is not null)
            {
                return linked;
            }
        }

        // 3. Verified-email claim
        if (IsEmailVerified(userInfo) && !string.IsNullOrWhiteSpace(userInfo.Email))
        {
            var pending = await _userRepository.GetUnlinkedByEmailAsync(userInfo.Email, cancellationToken);
            if (pending.Success && pending.Data is [var only])
            {
                var linked = await TryLinkAsync(only.Id, userInfo, cancellationToken);
                if (linked is not null)
                {
                    return linked;
                }
            }
            else if (pending.Data is { Count: > 1 })
            {
                // Ambiguous: leave them for their invitations to claim
                _logger.LogInformation(
                    "Sign-in: {Count} pending users share the signed-in email; none claimed by email", pending.Data.Count);
            }
        }

        // 4. New user
        return await CreateAsync(userInfo, cancellationToken);
    }

    private async Task<UserDto?> FindByIdentityAsync(string externalUserId, CancellationToken cancellationToken)
    {
        var result = await _userRepository.GetByExternalUserIdAsync(externalUserId, cancellationToken);
        return result.Success ? result.Data : null;
    }

    /// <summary>
    /// Links the pending user to the identity. Null when it can no longer be linked (claimed by a
    /// concurrent sign-in, or already linked); a concurrent claim by the same identity is re-read.
    /// </summary>
    private async Task<UserDto?> TryLinkAsync(Guid pendingUserId, ExternalUserInfo userInfo, CancellationToken cancellationToken)
    {
        var linked = await _userRepository.LinkIdentityAsync(
            pendingUserId, userInfo.ExternalUserId, userInfo.DisplayName, cancellationToken);
        if (linked.Success && linked.Data is not null)
        {
            _logger.LogInformation("Sign-in: pending user {UserId} claimed by its identity", pendingUserId);
            return linked.Data;
        }

        return await FindByIdentityAsync(userInfo.ExternalUserId, cancellationToken);
    }

    private async Task MergeAsync(Guid pendingUserId, Guid userId, CancellationToken cancellationToken)
    {
        var merged = await _userRepository.MergePendingUserAsync(pendingUserId, userId, cancellationToken);
        if (!merged.Success || merged.Data is null)
        {
            // Already linked or merged (a repeated acceptance): nothing to move
            _logger.LogInformation(
                "Sign-in: pending user {PendingUserId} not merged into {UserId}: {Reason}",
                pendingUserId, userId, merged.Message);
            return;
        }

        foreach (var tenantId in merged.Data)
        {
            _accessRevoker.EvictPermissions(userId, tenantId);
        }

        // One event for the merge; a user spans tenants, so TenantId is not set
        await _accessRevoker.PublishSafelyAsync(
            new PendingUserMerged { FromUserId = pendingUserId, ToUserId = userId, UserId = userId },
            cancellationToken);

        _logger.LogInformation("Sign-in: pending user {PendingUserId} merged into {UserId}", pendingUserId, userId);
    }

    private async Task<UserDto> CreateAsync(ExternalUserInfo userInfo, CancellationToken cancellationToken)
    {
        var created = await _userRepository.AddAsync(
            new UserDto(Guid.Empty, userInfo.ExternalUserId, userInfo.Email, userInfo.DisplayName ?? userInfo.Email, IsActive: true),
            cancellationToken);
        if (created.Success && created.Data is not null)
        {
            _logger.LogInformation("Sign-in: created user {UserId}", created.Data.Id);
            return created.Data;
        }

        // A concurrent first sign-in created the same identity: re-read it
        var existing = await FindByIdentityAsync(userInfo.ExternalUserId, cancellationToken);
        return existing ?? throw new InvalidOperationException(
            $"Failed to resolve or create user for external ID '{userInfo.ExternalUserId}'.");
    }

    private static bool IsEmailVerified(ExternalUserInfo userInfo)
        => userInfo.Attributes is not null
           && userInfo.Attributes.TryGetValue(EmailVerifiedAttribute, out var value)
           && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
