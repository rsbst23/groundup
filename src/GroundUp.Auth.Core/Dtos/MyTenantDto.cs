namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// One tenant the current user is an active member of, as listed by <c>GET /auth/me/tenants</c>.
/// </summary>
/// <param name="Id">The tenant id.</param>
/// <param name="Name">The tenant name.</param>
/// <param name="Slug">The tenant slug.</param>
/// <param name="Type"><c>standard</c> or <c>enterprise</c>.</param>
/// <param name="Switchable">True for a standard tenant (switch with <c>POST /auth/set-tenant</c>). False for an
/// enterprise tenant, which needs a sign-in through its own realm (its subdomain).</param>
/// <param name="Current">For a standard tenant, whether it is the session's current tenant. Null for an enterprise tenant.</param>
/// <param name="Subdomain">For an enterprise tenant, the subdomain to sign in at. Null for a standard tenant.</param>
public sealed record MyTenantDto(
    Guid Id,
    string Name,
    string Slug,
    string Type,
    bool Switchable,
    bool? Current,
    string? Subdomain);
