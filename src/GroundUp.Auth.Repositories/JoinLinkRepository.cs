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
/// EF Core <see cref="IJoinLinkRepository"/>.
/// <para>
/// SYSTEM BYPASS: queries the DbContext directly with explicit tenant filters, because links are used
/// by token before any tenant is selected. Soft-deleted links are excluded by the global query filter.
/// Conditional updates use <c>ExecuteUpdateAsync</c>, which bypasses the audit interceptor, so they
/// set <c>UpdatedAt</c>.
/// </para>
/// </summary>
public sealed class JoinLinkRepository : IJoinLinkRepository
{
    private readonly DbContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="JoinLinkRepository"/>.
    /// </summary>
    /// <param name="context">The EF Core database context.</param>
    public JoinLinkRepository(DbContext context)
    {
        _context = context;
    }

    private DbSet<JoinLink> JoinLinks => _context.Set<JoinLink>();

    /// <inheritdoc />
    public async Task<OperationResult<JoinLinkDto>> AddAsync(JoinLinkDto link, string tokenHash, CancellationToken cancellationToken = default)
    {
        var entity = new JoinLink
        {
            TenantId = link.TenantId,
            RoleId = link.RoleId,
            CreatedByUserId = link.CreatedByUserId,
            TokenHash = tokenHash,
            ExpiresAt = link.ExpiresAt,
            MaxUses = link.MaxUses,
            Status = JoinLinkStatus.Active
        };
        JoinLinks.Add(entity);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.Entry(entity).State = EntityState.Detached;
            return OperationResult<JoinLinkDto>.Fail("The join link could not be saved.", 409, ErrorCodes.Conflict);
        }

        return await GetByIdAsync(link.TenantId, entity.Id, cancellationToken) is { Success: true } saved
            ? OperationResult<JoinLinkDto>.Ok(saved.Data, statusCode: 201)
            : OperationResult<JoinLinkDto>.Fail("The join link was saved but could not be read back.", 500);
    }

    /// <inheritdoc />
    public Task<OperationResult<JoinLinkDto>> GetByIdAsync(Guid tenantId, Guid joinLinkId, CancellationToken cancellationToken = default)
        => SingleAsync(JoinLinks.Where(l => l.TenantId == tenantId && l.Id == joinLinkId), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<JoinLinkDto>> GetByIdAnyTenantAsync(Guid joinLinkId, CancellationToken cancellationToken = default)
        => SingleAsync(JoinLinks.Where(l => l.Id == joinLinkId), cancellationToken);

    /// <inheritdoc />
    public Task<OperationResult<JoinLinkDto>> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => SingleAsync(JoinLinks.Where(l => l.TokenHash == tokenHash), cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<PaginatedData<JoinLinkDto>>> ListAsync(
        Guid tenantId, PaginationParams paging, CancellationToken cancellationToken = default)
    {
        var query = JoinLinks.AsNoTracking().Where(l => l.TenantId == tenantId);
        var total = await query.CountAsync(cancellationToken);
        var items = await Project(query
                .OrderByDescending(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .Skip((paging.PageNumber - 1) * paging.PageSize)
                .Take(paging.PageSize))
            .ToListAsync(cancellationToken);

        return OperationResult<PaginatedData<JoinLinkDto>>.Ok(new PaginatedData<JoinLinkDto>
        {
            Items = items,
            PageNumber = paging.PageNumber,
            PageSize = paging.PageSize,
            TotalRecords = total
        });
    }

    /// <inheritdoc />
    public async Task<OperationResult> ReissueAsync(
        Guid tenantId, Guid joinLinkId, string tokenHash, Guid regeneratedByUserId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var updated = await ActiveById(tenantId, joinLinkId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.TokenHash, tokenHash)
                .SetProperty(l => l.CreatedByUserId, regeneratedByUserId)
                .SetProperty(l => l.UpdatedAt, now)
                .SetProperty(l => l.UpdatedBy, regeneratedByUserId.ToString()),
                cancellationToken);
        return updated == 1 ? OperationResult.Ok() : await NotActiveAsync(tenantId, joinLinkId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RevokeAsync(
        Guid tenantId, Guid joinLinkId, Guid revokedByUserId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var updated = await ActiveById(tenantId, joinLinkId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Status, JoinLinkStatus.Revoked)
                .SetProperty(l => l.RevokedAt, now)
                .SetProperty(l => l.RevokedByUserId, revokedByUserId)
                .SetProperty(l => l.UpdatedAt, now)
                .SetProperty(l => l.UpdatedBy, revokedByUserId.ToString()),
                cancellationToken);
        return updated == 1 ? OperationResult.Ok() : await NotActiveAsync(tenantId, joinLinkId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryUseAsync(Guid joinLinkId, CancellationToken cancellationToken = default)
    {
        // One statement: the database checks the limits and increments atomically
        var now = DateTime.UtcNow;
        var updated = await JoinLinks
            .Where(l => l.Id == joinLinkId
                && l.Status == JoinLinkStatus.Active
                && (l.ExpiresAt == null || l.ExpiresAt > now)
                && (l.MaxUses == null || l.UseCount < l.MaxUses))
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.UseCount, l => l.UseCount + 1)
                .SetProperty(l => l.UpdatedAt, now),
                cancellationToken);
        return updated == 1;
    }

    private IQueryable<JoinLink> ActiveById(Guid tenantId, Guid joinLinkId)
        => JoinLinks.Where(l => l.TenantId == tenantId && l.Id == joinLinkId && l.Status == JoinLinkStatus.Active);

    private async Task<OperationResult> NotActiveAsync(Guid tenantId, Guid joinLinkId, CancellationToken cancellationToken)
    {
        var exists = await JoinLinks.AsNoTracking().AnyAsync(l => l.TenantId == tenantId && l.Id == joinLinkId, cancellationToken);
        return exists
            ? OperationResult.Fail("The join link is revoked.", 409, ErrorCodes.Conflict)
            : OperationResult.NotFound("Join link not found.");
    }

    private async Task<OperationResult<JoinLinkDto>> SingleAsync(IQueryable<JoinLink> query, CancellationToken cancellationToken)
    {
        var dto = await Project(query.AsNoTracking()).FirstOrDefaultAsync(cancellationToken);
        return dto is null ? OperationResult<JoinLinkDto>.NotFound("Join link not found.") : OperationResult<JoinLinkDto>.Ok(dto);
    }

    /// <summary>
    /// Projects to the DTO with the role name and the reported status.
    /// </summary>
    private IQueryable<JoinLinkDto> Project(IQueryable<JoinLink> query)
    {
        var now = DateTime.UtcNow;
        return query.Select(l => new JoinLinkDto(
            l.Id,
            l.TenantId,
            l.RoleId,
            _context.Set<Role>().Where(r => r.Id == l.RoleId).Select(r => r.Name).FirstOrDefault(),
            l.CreatedByUserId,
            l.ExpiresAt,
            l.MaxUses,
            l.UseCount,
            l.Status != JoinLinkStatus.Active ? l.Status
                : l.ExpiresAt != null && l.ExpiresAt <= now ? JoinLinkStatus.Expired
                : l.MaxUses != null && l.UseCount >= l.MaxUses ? JoinLinkStatus.UsedUp
                : JoinLinkStatus.Active,
            l.RevokedAt,
            l.CreatedAt));
    }
}
