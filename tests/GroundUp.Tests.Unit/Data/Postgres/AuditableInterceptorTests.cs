using GroundUp.Core.Abstractions;
using GroundUp.Data.Postgres.Interceptors;
using GroundUp.Tests.Unit.Data.Postgres.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GroundUp.Tests.Unit.Data.Postgres;

/// <summary>
/// Unit tests for <see cref="AuditableInterceptor"/>.
/// Tests are wired through the DbContext options so SaveChangesAsync triggers the interceptor.
/// </summary>
public sealed class AuditableInterceptorTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private static TestGroundUpDbContext CreateContextWithInterceptor(IServiceProvider serviceProvider)
    {
        var interceptor = new AuditableInterceptor(serviceProvider);
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
    public async Task SavingChangesAsync_AddedAuditableEntity_SetsCreatedAtAndCreatedBy()
    {
        // Arrange
        var sp = CreateServiceProviderWithUser(TestUserId);
        using var context = CreateContextWithInterceptor(sp);
        var entity = new AuditableTestEntity { Name = "Test" };
        context.AuditableTestEntities.Add(entity);

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.True((DateTime.UtcNow - entity.CreatedAt).Duration() < TimeSpan.FromSeconds(1));
        Assert.Equal(TestUserId.ToString(), entity.CreatedBy);
    }

    [Fact]
    public async Task SavingChangesAsync_ModifiedAuditableEntity_SetsUpdatedAtAndUpdatedBy()
    {
        // Arrange
        var sp = CreateServiceProviderWithUser(TestUserId);
        using var context = CreateContextWithInterceptor(sp);
        var entity = new AuditableTestEntity { Name = "Original" };
        context.AuditableTestEntities.Add(entity);
        await context.SaveChangesAsync();

        // Act
        entity.Name = "Modified";
        context.Entry(entity).State = EntityState.Modified;
        await context.SaveChangesAsync();

        // Assert
        Assert.NotNull(entity.UpdatedAt);
        Assert.True((DateTime.UtcNow - entity.UpdatedAt!.Value).Duration() < TimeSpan.FromSeconds(1));
        Assert.Equal(TestUserId.ToString(), entity.UpdatedBy);
    }

    [Fact]
    public async Task SavingChangesAsync_NonAuditableEntity_DoesNotModify()
    {
        // Arrange
        var sp = CreateServiceProviderWithUser(TestUserId);
        using var context = CreateContextWithInterceptor(sp);
        var entity = new NonAuditableTestEntity { Name = "Test" };
        context.NonAuditableTestEntities.Add(entity);

        // Act
        await context.SaveChangesAsync();

        // Assert — entity saved successfully, no audit fields exist to check
        var saved = await context.NonAuditableTestEntities.FirstAsync();
        Assert.Equal("Test", saved.Name);
    }

    [Fact]
    public async Task SavingChangesAsync_NoCurrentUser_SetsTimestampsLeavesUserFieldsNull()
    {
        // Arrange
        var sp = CreateServiceProviderWithUser(userId: null);
        using var context = CreateContextWithInterceptor(sp);
        var entity = new AuditableTestEntity { Name = "Test" };
        context.AuditableTestEntities.Add(entity);

        // Act
        await context.SaveChangesAsync();

        // Assert
        Assert.True((DateTime.UtcNow - entity.CreatedAt).Duration() < TimeSpan.FromSeconds(1));
        Assert.Null(entity.CreatedBy);
    }
}
