namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request body for the POST /auth/organizations endpoint, which lets an already
/// authenticated user create an additional organization.
/// </summary>
/// <param name="OrganizationName">
/// The display name of the new organization (required, at most 200 characters).
/// The slug is derived from it.
/// </param>
public sealed record CreateOrganizationRequestDto(string? OrganizationName = null);
