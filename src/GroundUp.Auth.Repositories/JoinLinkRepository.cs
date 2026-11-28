using GroundUp.Auth.Core.Dtos;
using System.Linq.Expressions;
using GroundUp.Auth.Core.Entities;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Data.Abstractions;
using GroundUp.Core;
using GroundUp.Core.Models;
using GroundUp.Core.Results;
using GroundUp.Repositories;
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
        Guid tenantId, FilterParams filterParams, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var roles = _context.Set<Role>();
        Expression<Func<JoinLink, string?>> roleName = l => roles.Where(r => r.Id == l.RoleId).Select(r => r.Name).FirstOrDefault();
        var options = new ListQueryOptions<JoinLink>
        {
            Fields = new ListFieldMap<JoinLink>()
                .Sort("roleName", roleName)
                .SortAndFilter("status", EffectiveStatus(now))
                .Filter("roleId", l => l.RoleId)
                .Sort("useCount", l => l.UseCount)
                .Sort("expiresAt", l => l.ExpiresAt)
                .Sort("createdAt", l => l.CreatedAt)
                .Search(roleName),
            DefaultOrder = q => q.OrderByDescending(l => l.CreatedAt),
            TieBreaker = l => l.Id
        };

        var page = await JoinLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .ToPagedListAsync(filterParams, Projection(now), options, cancellationToken);
        return OperationResult<PaginatedData<JoinLinkDto>>.Ok(page);
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
    /// The status the lists report: the stored status unless Active, then Expired past the expiry,
    /// UsedUp at the use limit, otherwise Active. Filters and sorts on it; <see cref="Projection"/>
    /// reports the same value.
    /// </summary>
    private static Expression<Func<JoinLink, JoinLinkStatus>> EffectiveStatus(DateTime now)
        => l => l.Status != JoinLinkStatus.Active ? l.Status
            : l.ExpiresAt != null && l.ExpiresAt <= now ? JoinLinkStatus.Expired
            : l.MaxUses != null && l.UseCount >= l.MaxUses ? JoinLinkStatus.UsedUp
            : JoinLinkStatus.Active;

    private IQueryable<JoinLinkDto> Project(IQueryable<JoinLink> query) => query.Select(Projection(DateTime.UtcNow));

    /// <summary>
    /// Projects to the DTO with the role name and the reported status (see <see cref="EffectiveStatus"/>).
    /// </summary>
    private Expression<Func<JoinLink, JoinLinkDto>> Projection(DateTime now)
    {
        var roles = _context.Set<Role>();
        return l => new JoinLinkDto(
            l.Id,
            l.TenantId,
            l.RoleId,
            roles.Where(r => r.Id == l.RoleId).Select(r => r.Name).FirstOrDefault(),
            l.CreatedByUserId,
            l.ExpiresAt,
            l.MaxUses,
            l.UseCount,
            l.Status != JoinLinkStatus.Active ? l.Status
                : l.ExpiresAt != null && l.ExpiresAt <= now ? JoinLinkStatus.Expired
                : l.MaxUses != null && l.UseCount >= l.MaxUses ? JoinLinkStatus.UsedUp
                : JoinLinkStatus.Active,
            l.RevokedAt,
            l.CreatedAt);
    }
}
