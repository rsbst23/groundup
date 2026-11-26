using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using FluentValidation;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Auth.Services.Roles;
using GroundUp.Data.Abstractions;
using Microsoft.Extensions.Logging;

namespace GroundUp.Auth.Services;

/// <summary>
/// Default implementation of <see cref="IOrganizationProvisioningService"/>.
/// Logic was extracted from <see cref="NewOrganizationFlowHandler"/> so the sign-up callback
/// and the authenticated organization-creation endpoint share one implementation.
/// </summary>
internal sealed class OrganizationProvisioningService : IOrganizationProvisioningService
{
    /// <summary>
    /// Maximum number of slug attempts (the original slug plus disambiguated variants) before giving up.
    /// </summary>
    private const int MaxSlugRetries = 5;

    private readonly ITenantRepository _tenantRepository;
    private readonly IUserTenantRepository _userTenantRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IRoleGrantWriter _roleGrantWriter;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<OrganizationProvisioningService> _logger;
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _userRepository;
    private readonly IValidator<CreateOrganizationRequestDto> _createValidator;

    /// <summary>
    /// Initializes a new instance of <see cref="OrganizationProvisioningService"/>.
    /// </summary>
    public OrganizationProvisioningService(
        ITenantRepository tenantRepository,
        IUserTenantRepository userTenantRepository,
        IRoleRepository roleRepository,
        IRoleGrantWriter roleGrantWriter,
        IUnitOfWork unitOfWork,
        TenantContext tenantContext,
        ILogger<OrganizationProvisioningService> logger,
        ICurrentUser currentUser,
        IUserRepository userRepository,
        IValidator<CreateOrganizationRequestDto> createValidator)
    {
        _tenantRepository = tenantRepository;
        _userTenantRepository = userTenantRepository;
        _roleRepository = roleRepository;
        _roleGrantWriter = roleGrantWriter;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _logger = logger;
        _currentUser = currentUser;
        _userRepository = userRepository;
        _createValidator = createValidator;
    }

    /// <inheritdoc />
    public async Task<OperationResult<OrganizationProvisioningResult>> CreateForCurrentUserAsync(
        CreateOrganizationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        // Who is asking comes first: an unauthenticated caller learns nothing about the validation rules
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty)
        {
            return OperationResult<OrganizationProvisioningResult>.Unauthorized();
        }

        // The membership row stores the user's identity-provider subject, which the token does not carry
        var userResult = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (!userResult.Success || userResult.Data is null || !userResult.Data.IsActive)
        {
            return OperationResult<OrganizationProvisioningResult>.Unauthorized();
        }

        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return OperationResult<OrganizationProvisioningResult>.BadRequest(
                validation.Errors[0].ErrorMessage,
                validation.Errors.Select(e => e.ErrorMessage).ToList());
        }

        return await ProvisionAsync(
            new OrganizationProvisioningRequest(userId, userResult.Data.ExternalUserId, request.OrganizationName!.Trim()),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<OrganizationProvisioningResult>> ProvisionAsync(
        OrganizationProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        var organizationName = request.OrganizationName;
        var baseSlug = SlugGenerator.FromOrganizationName(organizationName);

        var previousTenantId = _tenantContext.TenantId;
        var tenantId = Guid.Empty;
        var slug = baseSlug;
        var provisioned = false;
        RoleGrantResult? adminGrant = null;

        try
        {
            for (var attempt = 1; attempt <= MaxSlugRetries; attempt++)
            {
                slug = attempt == 1 ? baseSlug : SlugGenerator.Disambiguate(baseSlug, attempt);

                try
                {
                    var transactionResult = await _unitOfWork.ExecuteInTransactionAsync(
                        ct => CreateOrganizationRowsAsync(request, slug, id => tenantId = id, grant => adminGrant = grant, ct),
                        cancellationToken);

                    if (transactionResult.Success)
                    {
                        provisioned = true;
                        break;
                    }

                    // A failed (rolled back) transaction is reported by the unit of work for unique
                    // constraint violations. Retry with the next disambiguated slug.
                    _logger.LogDebug(
                        "Organization provisioning transaction failed on slug '{Slug}' (attempt {Attempt}), retrying",
                        slug, attempt);
                }
                catch (SlugCollisionException)
                {
                    _logger.LogDebug(
                        "Slug collision on '{Slug}' (attempt {Attempt}), retrying",
                        slug, attempt);
                }
            }
        }
        finally
        {
            // The context is only pointed at the new tenant while the tenant-scoped rows are written.
            _tenantContext.TenantId = previousTenantId;
        }

        if (!provisioned)
        {
            _logger.LogError(
                "Organization provisioning failed: slug unavailable after {MaxRetries} attempts for org '{OrgName}'",
                MaxSlugRetries, organizationName);

            return OperationResult<OrganizationProvisioningResult>.Fail(
                "Unable to create organization — slug unavailable after retries.",
                409,
                "SLUG_COLLISION");
        }

        if (adminGrant is not null)
        {
            await _roleGrantWriter.PublishGrantedAsync(adminGrant, cancellationToken);
        }

        _logger.LogInformation(
            "Organization provisioned: TenantId={TenantId}, UserId={UserId}",
            tenantId, request.UserId);

        return OperationResult<OrganizationProvisioningResult>.Ok(
            new OrganizationProvisioningResult(tenantId, organizationName, slug));
    }

    /// <summary>
    /// Writes the tenant, membership, TenantAdmin role and role assignment. Runs inside the
    /// unit-of-work transaction; any failure throws so the transaction rolls back.
    /// </summary>
    private async Task CreateOrganizationRowsAsync(
        OrganizationProvisioningRequest request,
        string slug,
        Action<Guid> onTenantCreated,
        Action<RoleGrantResult> onAdminGranted,
        CancellationToken ct)
    {
        var tenantDto = new TenantDto(
            Id: Guid.NewGuid(),
            Name: request.OrganizationName,
            Slug: slug,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: null,
            IsActive: true);

        var tenantResult = await _tenantRepository.AddAsync(tenantDto, ct);
        if (!tenantResult.Success || tenantResult.Data is null)
        {
            // The only expected reason for a tenant insert to fail is a slug unique-constraint violation.
            throw new SlugCollisionException(slug);
        }

        var tenantId = tenantResult.Data.Id;
        onTenantCreated(tenantId);

        // Point the tenant context at the new tenant so tenant-scoped repositories write to it.
        _tenantContext.TenantId = tenantId;

        var membershipDto = new UserTenantDto(
            Id: Guid.NewGuid(),
            UserId: request.UserId,
            TenantId: tenantId,
            ExternalUserId: request.ExternalUserId,
            Status: MembershipStatus.Active);

        var membershipResult = await _userTenantRepository.AddAsync(membershipDto, ct);
        if (!membershipResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to create membership for user '{request.UserId}' in tenant '{tenantId}': {membershipResult.Message}");
        }

        var roleDto = new RoleDto(
            Id: Guid.NewGuid(),
            Name: AuthRoleNames.TenantAdmin,
            Description: "Full access within this tenant",
            RoleType: RoleType.Application,
            TenantId: tenantId,
            IsSystem: true);

        var roleResult = await _roleRepository.AddAsync(roleDto, ct);
        if (!roleResult.Success || roleResult.Data is null)
        {
            throw new InvalidOperationException(
                $"Failed to create TenantAdmin role for tenant '{tenantId}': {roleResult.Message}");
        }

        // A grant the system makes on the creator's behalf (source Provisioning), not one checked
        // against the creator's roles. Events wait until the transaction commits.
        var grantResult = await _roleGrantWriter.GrantAsync(
            request.UserId, roleResult.Data.Id, tenantId, RoleAssignmentSource.Provisioning, deferEvents: true, ct);
        if (!grantResult.Success || grantResult.Data is null)
        {
            throw new InvalidOperationException(
                $"Failed to assign TenantAdmin role to user '{request.UserId}' in tenant '{tenantId}': {grantResult.Message}");
        }

        onAdminGranted(grantResult.Data);
    }

    /// <summary>
    /// Marker exception used to detect slug collisions within the transaction.
    /// </summary>
    private sealed class SlugCollisionException : Exception
    {
        public SlugCollisionException(string slug)
            : base($"Slug collision on '{slug}'")
        {
        }
    }
}
