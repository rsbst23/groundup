using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities;
using GroundUp.Data.Abstractions;
using GroundUp.Repositories.Mappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GroundUp.Repositories.Setup;

/// <summary>
/// <see cref="ISetupTransactionLogRepository"/> over the application's DbContext.
/// </summary>
public sealed class SetupTransactionLogRepository : ISetupTransactionLogRepository
{
    private const string FirstAdminOperation = "first-admin-create";
    private const string KeycloakPendingStage = "keycloak-pending";
    private const string DbPendingStage = "db-pending";
    private const string FailedStage = "failed";

    private readonly DbContext _context;
    private readonly ILogger<SetupTransactionLogRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupTransactionLogRepository"/> class.
    /// </summary>
    /// <param name="context">The application's DbContext (contains <see cref="SetupTransactionLog"/>).</param>
    /// <param name="logger">Logger for rotation warnings.</param>
    public SetupTransactionLogRepository(DbContext context, ILogger<SetupTransactionLogRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    private DbSet<SetupTransactionLog> Logs => _context.Set<SetupTransactionLog>();

    /// <inheritdoc />
    public async Task<Guid> InsertWithRotationAsync(
        CreateSetupTransactionLogDto row, int maxRowCount, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        if (maxRowCount > 0)
        {
            var total = await Logs.CountAsync(cancellationToken);
            var toDelete = (total + 1) - maxRowCount;
            if (toDelete > 0)
            {
                var deletable = await Logs
                    .Where(l => l.Stage != KeycloakPendingStage && l.Stage != DbPendingStage)
                    .OrderBy(l => l.CreatedAt)
                    .Take(toDelete)
                    .ToListAsync(cancellationToken);

                if (deletable.Count < toDelete)
                {
                    _logger.LogWarning(
                        "Transaction log rotation could not free enough rows: total={Total}, max={Max}, pending protected.",
                        total, maxRowCount);
                }

                Logs.RemoveRange(deletable);
            }
        }

        var entity = SetupTransactionLogMapper.ToEntity(row);
        Logs.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return entity.Id;
    }

    /// <inheritdoc />
    public async Task UpdateStageAsync(
        Guid id, string stage, string? externalUserId = null, string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await Logs.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Stage = stage;
        if (externalUserId is not null)
        {
            entity.ExternalUserId = externalUserId;
        }

        if (errorMessage is not null)
        {
            entity.ErrorMessage = errorMessage;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Guid?> GetLatestUnfinishedFirstAdminIdAsync(CancellationToken cancellationToken = default)
    {
        return await Logs
            .AsNoTracking()
            .Where(l => l.Operation == FirstAdminOperation
                     && (l.Stage == KeycloakPendingStage || l.Stage == DbPendingStage || l.Stage == FailedStage))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SetupTransactionLogDto>> GetRecentAsync(
        int count, CancellationToken cancellationToken = default)
    {
        return await Logs
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Take(count)
            .ProjectToDto()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SetupTransactionLogDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await Logs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        return entity is null ? null : SetupTransactionLogMapper.ToDto(entity);
    }
}
