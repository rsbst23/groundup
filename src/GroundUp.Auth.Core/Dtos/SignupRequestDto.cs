namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// Request body for the POST /auth/signup endpoint.
/// </summary>
/// <param name="OrganizationName">
/// The name of the organization to create. When null, indicates a simple signup
/// (user joins the default tenant rather than creating a new organization).
/// </param>
public sealed record SignupRequestDto(string? OrganizationName = null);
