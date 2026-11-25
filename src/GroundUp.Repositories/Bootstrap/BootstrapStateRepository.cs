using GroundUp.Core.Entities;
using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GroundUp.Repositories.Bootstrap;

/// <summary>
/// <see cref="IBootstrapStateRepository"/> over the application's DbContext. Concurrent completion is
/// detected by the row's optimistic concurrency token, configured by the database provider.
/// </summary>
public sealed class BootstrapStateRepository : IBootstrapStateRepository
{
    private readonly DbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="BootstrapStateRepository"/> class.
    /// </summary>
    /// <param name="context">The application's DbContext (contains <see cref="BootstrapState"/>).</param>
    public BootstrapStateRepository(DbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<bool?> GetIsCompleteAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<BootstrapState>()
            .AsNoTracking()
            .Where(s => s.Id == BootstrapState.SentinelId)
            .Select(s => (bool?)s.IsComplete)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BootstrapCompletionOutcome> TryCompleteAsync(
        Guid completedByUserId, CancellationToken cancellationToken = default)
    {
        var state = await _context.Set<BootstrapState>()
            .FirstOrDefaultAsync(s => s.Id == BootstrapState.SentinelId, cancellationToken);

        if (state is null)
        {
            return BootstrapCompletionOutcome.Missing;
        }

        if (state.IsComplete)
        {
            return BootstrapCompletionOutcome.AlreadyComplete;
        }

        state.IsComplete = true;
        state.CompletedAt = DateTime.UtcNow;
        state.CompletedBy = completedByUserId;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return BootstrapCompletionOutcome.Completed;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Drop the rejected change so later work in this scope does not retry it.
            _context.ChangeTracker.Clear();
            return BootstrapCompletionOutcome.ConcurrentUpdate;
        }
    }
}
