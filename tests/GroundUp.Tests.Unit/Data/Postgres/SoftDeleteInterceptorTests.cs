using GroundUp.Core.Abstractions;
using GroundUp.Data.Postgres.Interceptors;
using GroundUp.Tests.Unit.Data.Postgres.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GroundUp.Tests.Unit.Data.Postgres;

/// <summary>
/// Unit tests for <see cref="SoftDeleteInterceptor"/>.
/// Tests are wired through the DbContext options so SaveChangesAsync triggers the interceptor.
/// </summary>
public sealed class SoftDeleteInterceptorTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private static TestGroundUpDbContext CreateContextWithInterceptor(IServiceProvider serviceProvider)
    {
        var interceptor = new SoftDeleteInterceptor(serviceProvider);
        var options = new DbContextOptionsBuilder<TestGroundUpDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        return new TestGroundUpDbContext(options);
    }

    private static IServiceProvider CreateServiceProviderWithUser(Guid? userId = null)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var scope = Substitute.For<IServiceScope>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scopedProvider = Substitute.For<IServiceProvider>();

        serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(scopedProvider);

        if (userId.HasValue)
        {
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.UserId.Returns(userId.Value);
            scopedProvider.GetService(typeof(ICurrentUser)).Returns(currentUser);
        }
        else
        {
            scopedProvider.GetService(typeof(ICurrentUser)).Returns(null);
        }

        return serviceProvider;
    }

    [Fact]
    public async Task SavingChangesAsync_DeletedSoftDeletableEntity_ConvertsToModifiedAndSetsFields()
    {
        // Arrange
        var sp = CreateServiceProviderWithUser(TestUserId);
        using var context = CreateContextWithInterceptor(sp);
        var entity = new SoftDeletableAuditableTestEntity { Name = "Test" };
        context.SoftDeletableAuditableTestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Act — mark as deleted via Remove()
        context.SoftDeletableAuditableTestEntities.Remove(entity);
        await context.SaveChangesAsync();

        // Assert — entity should still exist in DB with soft delete fields set
        var saved = await context.SoftDeletableAuditableTestEntities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        Assert.NotNull(saved);
        Assert.True(saved.IsDeleted);
        Assert.NotNull(saved.DeletedAt);
        Assert.True((DateTime.UtcNow - saved.DeletedAt!.Value).Duration() < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SavingChangesAsync_DeletedNonSoftDeletableEntity_DoesNotModifyState()
    {
        // Arrange
        var sp = CreateServiceProviderWithUser(TestUserId);
        using var context = CreateContextWithInterceptor(sp);
        var entity = new NonAuditableTestEntity { Name = "Test" };
        context.NonAuditableTestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Act — mark as deleted via Remove()
        context.NonAuditableTestEntities.Remove(entity);
        await context.SaveChangesAsync();

        // Assert — entity should be actually deleted from DB
        var saved = await context.NonAuditableTestEntities
            .FirstOrDefaultAsync(e => e.Id == entity.Id);

        Assert.Null(saved);
    }

    [Fact]
    public async Task SavingChangesAsync_WithCurrentUser_SetsDeletedBy()
    {
        // Arrange
        var sp = CreateServiceProviderWithUser(TestUserId);
        using var context = CreateContextWithInterceptor(sp);
        var entity = new SoftDeletableAuditableTestEntity { Name = "Test" };
        context.SoftDeletableAuditableTestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Act
        context.SoftDeletableAuditableTestEntities.Remove(entity);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.SoftDeletableAuditableTestEntities
            .IgnoreQueryFilters()
            .FirstAsync(e => e.Id == entity.Id);

        Assert.Equal(TestUserId.ToString(), saved.DeletedBy);
    }

    [Fact]
    public async Task SavingChangesAsync_NoCurrentUser_SetsIsDeletedAndDeletedAtLeavesDeletedByNull()
    {
        // Arrange
        var sp = CreateServiceProviderWithUser(userId: null);
        using var context = CreateContextWithInterceptor(sp);
        var entity = new SoftDeletableAuditableTestEntity { Name = "Test" };
        context.SoftDeletableAuditableTestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Act
        context.SoftDeletableAuditableTestEntities.Remove(entity);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.SoftDeletableAuditableTestEntities
            .IgnoreQueryFilters()
            .FirstAsync(e => e.Id == entity.Id);

        Assert.True(saved.IsDeleted);
        Assert.NotNull(saved.DeletedAt);
        Assert.Null(saved.DeletedBy);
    }
}
