using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core.Results;

namespace GroundUp.Auth.Services.Token;

/// <summary>
/// Manages authentication session operations including tenant selection and token refresh.
/// Orchestrates between user-tenant membership queries and token generation to provide
/// a complete session lifecycle for authenticated users.
/// </summary>
public sealed class AuthSessionService : IAuthSessionService
{
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ITokenService _tokenService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthSessionService"/> class.
    /// </summary>
    /// <param name="userTenantRepository">Repository for querying user-tenant memberships.</param>
    /// <param name="tenantRepository">Repository for querying tenant details.</param>
    /// <param name="tokenService">Service for generating JWT tokens.</param>
    public AuthSessionService(
        IUserTenantRepository userTenantRepository,
        ITenantRepository tenantRepository,
        ITokenService tokenService)
    {
        _userTenantRepository = userTenantRepository;
        _tenantRepository = tenantRepository;
        _tokenService = tokenService;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SetTenantResponseDto>> SetTenantAsync(Guid userId, Guid? tenantId)
    {
        // 1. Query all tenant memberships for user
        var membershipsResult = await _userTenantRepository.GetAllMembershipsForUserAsync(userId);
        if (!membershipsResult.Success || membershipsResult.Data is null)
        {
            return OperationResult<SetTenantResponseDto>.Forbidden("User does not belong to any tenant");
        }

        var memberships = membershipsResult.Data;

        // 2. If no memberships → return Forbidden
        if (memberships.Count == 0)
        {
            return OperationResult<SetTenantResponseDto>.Forbidden("User does not belong to any tenant");
        }

        // 3. If tenantId is null → auto-select or return list
        if (tenantId is null)
        {
            if (memberships.Count == 1)
            {
                // Auto-select the single tenant
                var membership = memberships[0];
                var token = await _tokenService.GenerateTokenAsync(userId, membership.TenantId);

                if (token is null)
                {
                    return OperationResult<SetTenantResponseDto>.NotFound("User not found");
                }

                var response = new SetTenantResponseDto(false, null, token);
                return OperationResult<SetTenantResponseDto>.Ok(response);
            }

            // Multiple memberships — return tenant list (single batch query, bypassing tenant filter)
            var membershipTenantIds = memberships.Select(m => m.TenantId).ToList();
            var tenantsResult = await _tenantRepository.GetByIdsBypassFilterAsync(membershipTenantIds);
            var tenantsById = tenantsResult.Success && tenantsResult.Data is not null
                ? tenantsResult.Data.ToDictionary(t => t.Id)
                : new Dictionary<Guid, TenantDto>();

            var tenantList = new List<TenantListItemDto>();
            foreach (var membership in memberships)
            {
                if (tenantsById.TryGetValue(membership.TenantId, out var tenant))
                {
                    tenantList.Add(new TenantListItemDto(tenant.Id, tenant.Name, null));
                }
            }

            var multiResponse = new SetTenantResponseDto(true, tenantList, null);
            return OperationResult<SetTenantResponseDto>.Ok(multiResponse);
        }

        // 4. If tenantId specified → validate membership and issue token
        var belongsToTenant = memberships.Any(m => m.TenantId == tenantId.Value);
        if (!belongsToTenant)
        {
            return OperationResult<SetTenantResponseDto>.Forbidden("User does not belong to the specified tenant");
        }

        var selectedToken = await _tokenService.GenerateTokenAsync(userId, tenantId.Value);
        if (selectedToken is null)
        {
            return OperationResult<SetTenantResponseDto>.NotFound("User not found");
        }

        var selectedResponse = new SetTenantResponseDto(false, null, selectedToken);
        return OperationResult<SetTenantResponseDto>.Ok(selectedResponse);
    }

    /// <inheritdoc />
    public async Task<OperationResult<string>> RefreshTokenAsync(Guid userId, Guid tenantId)
    {
        // 1. Re-validate user still belongs to tenant
        var membershipsResult = await _userTenantRepository.GetAllMembershipsForUserAsync(userId);
        if (!membershipsResult.Success || membershipsResult.Data is null)
        {
            return OperationResult<string>.Forbidden("User no longer belongs to the specified tenant");
        }

        var belongsToTenant = membershipsResult.Data.Any(m => m.TenantId == tenantId);
        if (!belongsToTenant)
        {
            return OperationResult<string>.Forbidden("User no longer belongs to the specified tenant");
        }

        // 2. Generate new token with fresh roles
        var token = await _tokenService.GenerateTokenAsync(userId, tenantId);
        if (token is null)
        {
            return OperationResult<string>.NotFound("User not found");
        }

        return OperationResult<string>.Ok(token);
    }
}
