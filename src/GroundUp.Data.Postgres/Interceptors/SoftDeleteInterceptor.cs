using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Data.Postgres.Interceptors;

/// <summary>
/// SaveChanges interceptor that converts Remove() calls on <see cref="ISoftDeletable"/>
/// entities to soft deletes. Acts as a safety net for direct DbContext usage —
/// <see cref="GroundUp.Repositories.BaseRepository{TEntity, TDto}"/> already handles
/// soft delete explicitly.
/// Registered as a singleton; resolves <see cref="ICurrentUser"/> from a new DI scope per call.
/// </summary>
public sealed class SoftDeleteInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="SoftDeleteInterceptor"/>.
    /// </summary>
    /// <param name="serviceProvider">The root service provider for creating scopes.</param>
    public SoftDeleteInterceptor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return await base.SavingChangesAsync(eventData, result, cancellationToken);

        using var scope = _serviceProvider.CreateScope();
        var currentUser = scope.ServiceProvider.GetService<ICurrentUser>();
        var userId = currentUser?.UserId.ToString();
        var utcNow = DateTime.UtcNow;

        foreach (var entry in eventData.Context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State != EntityState.Deleted)
                continue;

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = utcNow;
            entry.Entity.DeletedBy = userId;
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
