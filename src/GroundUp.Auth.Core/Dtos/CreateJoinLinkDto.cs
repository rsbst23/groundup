namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request to create a join link for the current tenant.
/// </summary>
/// <param name="RoleId">The role to grant (a role in the current tenant).</param>
/// <param name="ExpiresAt">Optional expiry (UTC).</param>
/// <param name="MaxUses">Optional maximum number of people who may join with it.</param>
public sealed record CreateJoinLinkDto(Guid RoleId, DateTime? ExpiresAt = null, int? MaxUses = null);
