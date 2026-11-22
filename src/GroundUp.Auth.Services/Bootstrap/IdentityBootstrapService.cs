using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core;
using GroundUp.Core.Results;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services.Bootstrap;

/// <summary>
/// Provisions the first SuperAdmin user for the setup wizard. The decisions live here (missing role,
/// idempotent retry, conflicting attributes); the writes go through
/// <see cref="IIdentityBootstrapRepository.EnsureSuperAdminAsync"/>, which creates the user, membership
/// and role assignment in one transaction with existence checks, so a partial failure can be retried.
/// </summary>
public sealed class IdentityBootstrapService : IIdentityBootstrapService
{
    private readonly IIdentityBootstrapRepository _repository;
    private readonly ILogger<IdentityBootstrapService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="IdentityBootstrapService"/>.
    /// </summary>
    /// <param name="repository">System-level data access for first-run setup.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public IdentityBootstrapService(
        IIdentityBootstrapRepository repository,
        ILogger<IdentityBootstrapService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<BootstrapAdminResultDto>> ProvisionFirstSuperAdminAsync(
        ProvisionFirstSuperAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. The SuperAdmin role must exist (created by the system role seeder)
        var superAdminRoleId = await _repository.FindRoleIdByNameAsync(AuthRoleNames.SuperAdmin, cancellationToken);
        if (superAdminRoleId is null)
        {
            _logger.LogError("SuperAdmin role is missing from the database. Verify the auth seeders have run.");
            return OperationResult<BootstrapAdminResultDto>.Fail(
                "SuperAdmin role is missing — verify the auth seeders have run.", 500);
        }

        // 2. An existing user with this email is either a retry (same display name) or a conflict
        var existingUser = await _repository.FindUserByEmailAsync(request.Email, cancellationToken);
        if (existingUser is not null)
        {
            if (existingUser.DisplayName == request.DisplayName)
            {
                _logger.LogInformation(
                    "SuperAdmin user {Email} already exists with matching attributes. Returning idempotent success.",
                    request.Email);

                return OperationResult<BootstrapAdminResultDto>.Ok(
                    new BootstrapAdminResultDto(existingUser.Id, existingUser.Email, AlreadyExisted: true));
            }

            _logger.LogWarning(
                "A super admin user already exists with email {Email} but conflicting display name.",
                request.Email);

            return OperationResult<BootstrapAdminResultDto>.Fail(
                "A super admin user already exists with conflicting attributes.",
                409,
                ErrorCodes.Conflict);
        }

        // 3. Create user, membership and role assignment (one transaction, existence checks before each insert)
        var ensureResult = await _repository.EnsureSuperAdminAsync(
            new SuperAdminRecord(request.Email, request.DisplayName, request.ExternalUserId, request.TenantId, superAdminRoleId.Value),
            cancellationToken);

        if (!ensureResult.Success)
        {
            _logger.LogWarning(
                "Unique constraint violation while provisioning SuperAdmin user {Email}.",
                request.Email);

            return OperationResult<BootstrapAdminResultDto>.Fail(
                ensureResult.Message, ensureResult.StatusCode, ensureResult.ErrorCode);
        }

        _logger.LogInformation(
            "Provisioned SuperAdmin user {Email} with ID {UserId} in tenant {TenantId}.",
            request.Email, ensureResult.Data, request.TenantId);

        return OperationResult<BootstrapAdminResultDto>.Ok(
            new BootstrapAdminResultDto(ensureResult.Data, request.Email, AlreadyExisted: false));
    }

    /// <inheritdoc />
    public Task<bool> HasSuperAdminAsync(CancellationToken cancellationToken = default)
        => _repository.AnyUserHasRoleAsync(AuthRoleNames.SuperAdmin, cancellationToken);

    /// <inheritdoc />
    public Task<Guid?> GetSuperAdminUserIdAsync(CancellationToken cancellationToken = default)
        => _repository.GetFirstUserIdWithRoleAsync(AuthRoleNames.SuperAdmin, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasSystemTenantAsync(CancellationToken cancellationToken = default)
        => _repository.TenantExistsAsync(AuthRoleNames.SystemTenantId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasSuperAdminRoleAsync(CancellationToken cancellationToken = default)
        => _repository.RoleExistsAsync(AuthRoleNames.SuperAdmin, cancellationToken);
}
