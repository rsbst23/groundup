using FluentAssertions;

namespace GroundUp.Tests.Integration.Auth.Repositories;

/// <summary>
/// Integration tests verifying UserRepository lookup methods.
/// Property 4: UserRepository ExternalUserId lookup returns correct user.
/// Property 5: UserRepository email lookup is case-insensitive.
/// Validates: Requirements 9.2, 9.3, 9.4
/// </summary>
public sealed class UserRepositoryLookupTests : AuthIntegrationTestBase
{
    [Fact]
    public async Task GetByExternalUserIdAsync_ReturnsCorrectUser()
    {
        // Arrange
        var externalId = $"auth0|{Guid.NewGuid():N}";
        var userId = await SeedUserAsync(externalId, $"user-{Guid.NewGuid():N}@test.com", "Test User");
        var repo = CreateUserRepository();

        // Act
        var result = await repo.GetByExternalUserIdAsync(externalId);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(userId);
        result.Data!.ExternalUserId.Should().Be(externalId);
    }

    [Fact]
    public async Task GetByExternalUserIdAsync_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        await SeedUserAsync($"auth0|{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");
        var repo = CreateUserRepository();

        // Act
        var result = await repo.GetByExternalUserIdAsync("auth0|nonexistent");

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetByEmailAsync_ExactCase_ReturnsUser()
    {
        // Arrange
        var email = $"User-{Guid.NewGuid():N}@Example.com";
        var userId = await SeedUserAsync($"auth0|{Guid.NewGuid():N}", email, "Test User");
        var repo = CreateUserRepository();

        // Act
        var result = await repo.GetByEmailAsync(email);

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(userId);
    }

    [Fact]
    public async Task GetByEmailAsync_DifferentCase_ReturnsSameUser()
    {
        // Arrange
        var email = $"TestUser-{Guid.NewGuid():N}@Example.COM";
        var userId = await SeedUserAsync($"auth0|{Guid.NewGuid():N}", email, "Test User");
        var repo = CreateUserRepository();

        // Act — query with all lowercase
        var result = await repo.GetByEmailAsync(email.ToLower());

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(userId);
    }

    [Fact]
    public async Task GetByEmailAsync_UpperCase_ReturnsSameUser()
    {
        // Arrange
        var email = $"mixedcase-{Guid.NewGuid():N}@test.com";
        var userId = await SeedUserAsync($"auth0|{Guid.NewGuid():N}", email, "Test User");
        var repo = CreateUserRepository();

        // Act — query with all uppercase
        var result = await repo.GetByEmailAsync(email.ToUpper());

        // Assert
        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(userId);
    }

    [Fact]
    public async Task GetByEmailAsync_WithNonExistentEmail_ReturnsNotFound()
    {
        // Arrange
        await SeedUserAsync($"auth0|{Guid.NewGuid():N}", $"user-{Guid.NewGuid():N}@test.com");
        var repo = CreateUserRepository();

        // Act
        var result = await repo.GetByEmailAsync("nonexistent@nowhere.com");

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
