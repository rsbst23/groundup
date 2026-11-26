using FluentAssertions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroundUp.Tests.Integration.Auth.Keycloak;

/// <summary>
/// GU-68 Requirement 7.2: the account look-up that picks the sign-in or registration page for an
/// invitee, against a real Keycloak.
/// </summary>
[Collection("Keycloak")]
[Trait("Category", "Integration")]
public sealed class KeycloakFindUserByEmailTests
{
    private readonly KeycloakFixture _fixture;

    public KeycloakFindUserByEmailTests(KeycloakFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FindUserByEmailAsync_ExistingAccount_FoundIgnoringCase()
    {
        var service = CreateAdminService();
        var email = $"find-{Guid.NewGuid():N}@example.com";
        var created = await service.ProvisionUserAsync(
            KeycloakFixture.TestRealmName, new ProvisionUserRequest(email, "Find Me", "Pass12345!", RequirePasswordReset: false));
        created.Success.Should().BeTrue(created.Message);

        try
        {
            var result = await service.FindUserByEmailAsync(KeycloakFixture.TestRealmName, email.ToUpperInvariant());

            result.Success.Should().BeTrue(result.Message);
            result.Data.Should().NotBeNull();
            result.Data!.ExternalUserId.Should().Be(created.Data!.ExternalUserId);
            result.Data.Email.Should().Be(email);
        }
        finally
        {
            await service.DeleteUserAsync(KeycloakFixture.TestRealmName, created.Data!.ExternalUserId);
        }
    }

    [Fact]
    public async Task FindUserByEmailAsync_NoAccount_SucceedsWithNoData()
    {
        var result = await CreateAdminService().FindUserByEmailAsync(
            KeycloakFixture.TestRealmName, $"nobody-{Guid.NewGuid():N}@example.com");

        result.Success.Should().BeTrue(result.Message);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task FindUserByEmailAsync_PrefixOfAnotherEmail_NotAMatch()
    {
        // exact=true: "an@x" must not find "ana@x"
        var service = CreateAdminService();
        var suffix = Guid.NewGuid().ToString("N");
        var created = await service.ProvisionUserAsync(
            KeycloakFixture.TestRealmName, new ProvisionUserRequest($"ana-{suffix}@example.com", "Ana", "Pass12345!", RequirePasswordReset: false));

        try
        {
            var result = await service.FindUserByEmailAsync(KeycloakFixture.TestRealmName, $"an-{suffix}@example.com");

            result.Data.Should().BeNull();
        }
        finally
        {
            await service.DeleteUserAsync(KeycloakFixture.TestRealmName, created.Data!.ExternalUserId);
        }
    }

    private KeycloakIdentityProviderAdminService CreateAdminService()
        => new(_fixture.CreateHttpClientFactory(), _fixture.CreateOptionsMonitor(), _fixture.CreateAdminTokenCache(),
            NullLogger<KeycloakIdentityProviderAdminService>.Instance);
}
