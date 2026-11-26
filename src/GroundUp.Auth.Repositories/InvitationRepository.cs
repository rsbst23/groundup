using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Auth.Repositories;

/// <summary>
/// EF Core <see cref="IInvitationRepository"/>.
/// <para>
/// SYSTEM BYPASS: queries the DbContext directly with explicit tenant filters instead of the
/// tenant-scoped base repository, because invitations are looked up by token before any tenant is
/// selected. Soft-deleted invitations are excluded by the global query filter. Conditional updates use
/// <c>ExecuteUpdateAsync</c>, which bypasses the audit interceptor, so they set <c>UpdatedAt</c>.
/// </para>
/// </summary>
public sealed class InvitationRepository : IInvitationRepository
{
    private readonly DbContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="InvitationRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public InvitationRepository(DbContext context)
    {
        _context = context;
    }

    private DbSet<Invitation> Invitations => _context.Set<Invitation>();

    /// <inheritdoc />
    public async Task<OperationResult<InvitationDto>> AddAsync(
        InvitationDto invitation, string normalizedEmail, string tokenHash, CancellationToken cancellationToken = default)
    {
        var entity = new Invitation
        {
            TenantId = invitation.TenantId,
            Email = invitation.Email,
            NormalizedEmail = normalizedEmail,
            RoleId = invitation.RoleId,
            UserId = invitation.UserId,
            CreatedByUserId = invitation.CreatedByUserId,
            TokenHash = tokenHash,
            ExpiresAt = invitation.ExpiresAt,
            Status = InvitationStatus.Pending
        };
        Invitations.Add(entity);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The expected cause is the one-pending-invitation-per-email index
            _context.Entry(entity).State = EntityState.Detached;
            return OperationResult<InvitationDto>.Fail(
                "This email already has a pending invitation to the organization.", 409, ErrorCodes.Conflict);
        }

        return await GetByIdAsync(invitation.TenantId, entity.Id, cancellationToken) is { Success: true } saved
            ? OperationResult<InvitationDto>.Ok(saved.Data, statusCode: 201)
            : OperationResult<InvitationDto>.Fail("The invitation was saved but could not be read back.", 500);
    }

    /// <inheritdoc />
    public async Task<OperationResult<InvitationDto>> GetByIdAsync(
        Guid tenantId, Guid invitationId, CancellationToken cancellationToken = default)
    {
        var dto = await Project(Invitations.AsNoTracking().Where(i => i.TenantId == tenantId && i.Id == invitationId))
            .FirstOrDefaultAsync(cancellationToken);
        return dto is null ? OperationResult<InvitationDto>.NotFound("Invitation not found.") : OperationResult<InvitationDto>.Ok(dto);
    }

    /// <inheritdoc />
    public async Task<OperationResult<InvitationDto>> GetByTokenHashAsync(
        string tokenHash, CancellationToken cancellationToken = default)
    {
        var dto = await Project(Invitations.AsNoTracking().Where(i => i.TokenHash == tokenHash))
            .FirstOrDefaultAsync(cancellationToken);
        return dto is null ? OperationResult<InvitationDto>.NotFound("Invitation not found.") : OperationResult<InvitationDto>.Ok(dto);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<InvitationDto>>> ListAsync(
        Guid tenantId, PaginationParams paging, CancellationToken cancellationToken = default)
    {
        var query = Invitations.AsNoTracking().Where(i => i.TenantId == tenantId);
        var total = await query.CountAsync(cancellationToken);
        var items = await Project(query
                .OrderByDescending(i => i.CreatedAt)
                .ThenBy(i => i.Id)
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize))
            .ToListAsync(cancellationToken);

        return OperationResult<PaginatedData<InvitationDto>>.Ok(new PaginatedData<InvitationDto>
        {
            Items = items,
            PageNumber = paging.PageNumber,
            PageSize = paging.PageSize,
            TotalRecords = total
        });
    }

    /// <inheritdoc />
    public async Task<OperationResult> ReissueAsync(
        Guid tenantId, Guid invitationId, string tokenHash, DateTime expiresAt, Guid resentByUserId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var updated = await PendingById(tenantId, invitationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.TokenHash, tokenHash)
                .SetProperty(i => i.ExpiresAt, expiresAt)
                .SetProperty(i => i.CreatedByUserId, resentByUserId)
                .SetProperty(i => i.UpdatedAt, now)
                .SetProperty(i => i.UpdatedBy, resentByUserId.ToString()),
                cancellationToken);

        return updated == 1 ? OperationResult.Ok() : await NotPendingAsync(tenantId, invitationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RevokeAsync(
        Guid tenantId, Guid invitationId, Guid revokedByUserId, CancellationToken cancellationToken = default)
    {
        var updated = await Revoke(PendingById(tenantId, invitationId), revokedByUserId, cancellationToken);
        return updated == 1 ? OperationResult.Ok() : await NotPendingAsync(tenantId, invitationId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<List<Guid>>> RevokePendingForUserAsync(
        Guid tenantId, Guid userId, Guid revokedByUserId, CancellationToken cancellationToken = default)
    {
        var pending = Invitations.Where(i => i.TenantId == tenantId && i.UserId == userId && i.Status == InvitationStatus.Pending);
        var ids = await pending.Select(i => i.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return OperationResult<List<Guid>>.Ok([]);
        }

        await Revoke(pending.Where(i => ids.Contains(i.Id)), revokedByUserId, cancellationToken);
        return OperationResult<List<Guid>>.Ok(ids);
    }

    private IQueryable<Invitation> PendingById(Guid tenantId, Guid invitationId)
        => Invitations.Where(i => i.TenantId == tenantId && i.Id == invitationId && i.Status == InvitationStatus.Pending);

    private static Task<int> Revoke(IQueryable<Invitation> invitations, Guid revokedByUserId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return invitations.ExecuteUpdateAsync(s => s
            .SetProperty(i => i.Status, InvitationStatus.Revoked)
            .SetProperty(i => i.RevokedAt, now)
            .SetProperty(i => i.RevokedByUserId, revokedByUserId)
            .SetProperty(i => i.UpdatedAt, now)
            .SetProperty(i => i.UpdatedBy, revokedByUserId.ToString()),
            cancellationToken);
    }

    private async Task<OperationResult> NotPendingAsync(Guid tenantId, Guid invitationId, CancellationToken cancellationToken)
    {
        var exists = await Invitations.AsNoTracking().AnyAsync(i => i.TenantId == tenantId && i.Id == invitationId, cancellationToken);
        return exists
            ? OperationResult.Fail("The invitation is no longer pending.", 409, ErrorCodes.Conflict)
            : OperationResult.NotFound("Invitation not found.");
    }

    /// <summary>
    /// Projects to the DTO with the role name and the reported status (Expired for a Pending
    /// invitation past its expiry).
    /// </summary>
    private IQueryable<InvitationDto> Project(IQueryable<Invitation> query)
    {
        var now = DateTime.UtcNow;
        return query.Select(i => new InvitationDto(
            i.Id,
            i.TenantId,
            i.Email,
            i.RoleId,
            _context.Set<Role>().Where(r => r.Id == i.RoleId).Select(r => r.Name).FirstOrDefault(),
            i.UserId,
            i.CreatedByUserId,
            i.ExpiresAt,
            i.Status == InvitationStatus.Pending && i.ExpiresAt <= now ? InvitationStatus.Expired : i.Status,
            i.AcceptedByUserId,
            i.AcceptedAt,
            i.RevokedAt,
            i.CreatedAt));
    }
}
