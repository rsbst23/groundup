using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GroundUp.Data.Postgres.Interceptors;

/// <summary>
/// SaveChanges interceptor that auto-populates <see cref="IAuditable"/> fields.
/// Sets CreatedAt/CreatedBy on Added entities and UpdatedAt/UpdatedBy on Modified entities.
/// Registered as a singleton; resolves <see cref="ICurrentUser"/> from a new DI scope per call.
/// </summary>
public sealed class AuditableInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditableInterceptor"/>.
    /// </summary>
    /// <param name="serviceProvider">The root service provider for creating scopes.</param>
    public AuditableInterceptor(IServiceProvider serviceProvider)
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

        foreach (var entry in eventData.Context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = utcNow;
                    entry.Entity.CreatedBy = userId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = utcNow;
                    entry.Entity.UpdatedBy = userId;
                    break;
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
