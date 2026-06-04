using FluentAssertions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Results;
using GroundUp.Services.Setup;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Setup;

/// <summary>
/// Unit tests for the SetupPreconditions static helper.
/// Validates that each ordering rule returns the correct error when its predecessor is incomplete.
/// Requirements: 16.1–16.5
/// </summary>
public sealed class SetupPreconditionsTests
{
    #region CheckAppIdentityAsync

    [Fact]
    public async Task CheckAppIdentityAsync_WhenNameMissing_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(null!)));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));

        // Act
        var result = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("App identity");
    }

    [Fact]
    public async Task CheckAppIdentityAsync_WhenNameEmpty_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));

        // Act
        var result = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("App identity");
    }

    [Fact]
    public async Task CheckAppIdentityAsync_WhenNameGetFails_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Fail("Not found", 404)));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));

        // Act
        var result = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("App identity");
    }

    [Fact]
    public async Task CheckAppIdentityAsync_WhenDomainGetFails_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("My App")));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Fail("Not found", 404)));

        // Act
        var result = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("App identity");
    }

    [Fact]
    public async Task CheckAppIdentityAsync_WhenBothSettingsPresent_ReturnsNull()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("My App")));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));

        // Act
        var result = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckAppIdentityAsync_WhenDomainEmpty_ReturnsNull()
    {
        // Arrange — domain may be empty string (host-only cookie), that's valid
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("My App")));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));

        // Act
        var result = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CheckIdentityProviderAsync — Requires app-identity (Req 16.1)

    [Fact]
    public async Task CheckIdentityProviderAsync_WhenPublicBaseUrlMissing_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(null!)));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup")));

        // Act
        var result = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Identity provider");
    }

    [Fact]
    public async Task CheckIdentityProviderAsync_WhenPublicBaseUrlEmpty_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup")));

        // Act
        var result = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Identity provider");
    }

    [Fact]
    public async Task CheckIdentityProviderAsync_WhenRealmNameMissing_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("https://keycloak.example.com")));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(null!)));

        // Act
        var result = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Identity provider");
    }

    [Fact]
    public async Task CheckIdentityProviderAsync_WhenRealmNameEmpty_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("https://keycloak.example.com")));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));

        // Act
        var result = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Identity provider");
    }

    [Fact]
    public async Task CheckIdentityProviderAsync_WhenPublicBaseUrlGetFails_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Fail("Not found", 404)));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup")));

        // Act
        var result = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Identity provider");
    }

    [Fact]
    public async Task CheckIdentityProviderAsync_WhenBothSettingsPresent_ReturnsNull()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("https://keycloak.example.com")));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup")));

        // Act
        var result = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CheckKeycloakBootstrapAsync — Requires identity-provider (Req 16.2)

    [Fact]
    public async Task CheckKeycloakBootstrapAsync_WhenAdminClientIdMissing_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(null!)));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("some-secret")));

        // Act
        var result = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Keycloak admin client");
    }

    [Fact]
    public async Task CheckKeycloakBootstrapAsync_WhenAdminClientIdEmpty_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("some-secret")));

        // Act
        var result = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Keycloak admin client");
    }

    [Fact]
    public async Task CheckKeycloakBootstrapAsync_WhenAdminClientSecretMissing_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup-admin-client")));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(null!)));

        // Act
        var result = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Keycloak admin client");
    }

    [Fact]
    public async Task CheckKeycloakBootstrapAsync_WhenAdminClientSecretEmpty_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup-admin-client")));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));

        // Act
        var result = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Keycloak admin client");
    }

    [Fact]
    public async Task CheckKeycloakBootstrapAsync_WhenAdminClientIdGetFails_ReturnsError()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Fail("Not found", 404)));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("some-secret")));

        // Act
        var result = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("Keycloak admin client");
    }

    [Fact]
    public async Task CheckKeycloakBootstrapAsync_WhenBothSettingsPresent_ReturnsNull()
    {
        // Arrange
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup-admin-client")));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("super-secret-value")));

        // Act
        var result = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Step Ordering Integration — Verifying the chain (Req 16.1–16.4)

    /// <summary>
    /// Validates Req 16.1: identity-provider step requires app-identity to be completed.
    /// The SetupWizardService calls CheckAppIdentityAsync before SetIdentityProviderAsync.
    /// </summary>
    [Fact]
    public async Task IdentityProviderStep_WhenAppIdentityIncomplete_ReturnsAppIdentityError()
    {
        // Arrange — app-identity NOT completed (name is empty)
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));

        // Act — this is the precondition check that identity-provider step performs
        var error = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);

        // Assert
        error.Should().NotBeNull();
        error.Should().Contain("App identity");
        error.Should().Contain("completed first");
    }

    /// <summary>
    /// Validates Req 16.2: keycloak-bootstrap step requires identity-provider to be completed.
    /// The SetupWizardService calls CheckIdentityProviderAsync before BootstrapKeycloakAsync.
    /// </summary>
    [Fact]
    public async Task KeycloakBootstrapStep_WhenIdentityProviderIncomplete_ReturnsIdentityProviderError()
    {
        // Arrange — identity-provider NOT completed (public-base-url is empty)
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup")));

        // Act — this is the precondition check that keycloak-bootstrap step performs
        var error = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);

        // Assert
        error.Should().NotBeNull();
        error.Should().Contain("Identity provider");
        error.Should().Contain("completed first");
    }

    /// <summary>
    /// Validates Req 16.3: first-admin step requires keycloak-bootstrap to be completed.
    /// The SetupWizardService calls CheckKeycloakBootstrapAsync before CreateFirstAdminAsync.
    /// </summary>
    [Fact]
    public async Task FirstAdminStep_WhenKeycloakBootstrapIncomplete_ReturnsKeycloakError()
    {
        // Arrange — keycloak-bootstrap NOT completed (admin-client-id is empty)
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("some-secret")));

        // Act — this is the precondition check that first-admin step performs
        var error = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert
        error.Should().NotBeNull();
        error.Should().Contain("Keycloak admin client");
        error.Should().Contain("provisioned first");
    }

    /// <summary>
    /// Validates Req 16.4: complete step requires first-admin to be completed.
    /// The SetupWizardService calls all three precondition checks before CompleteSetupAsync.
    /// This test verifies the full chain: when all preconditions pass, all checks return null.
    /// </summary>
    [Fact]
    public async Task CompleteStep_WhenAllPreconditionsMet_AllChecksReturnNull()
    {
        // Arrange — all steps completed
        var settings = Substitute.For<ISettingsService>();

        // App identity completed
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("My App")));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));

        // Identity provider completed
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("https://keycloak.example.com")));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup")));

        // Keycloak bootstrap completed
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup-admin-client")));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("encrypted-secret")));

        // Act — all precondition checks that complete step performs
        var appIdentityError = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);
        var idpError = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);
        var kcError = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert
        appIdentityError.Should().BeNull();
        idpError.Should().BeNull();
        kcError.Should().BeNull();
    }

    /// <summary>
    /// Validates Req 16.4: complete step returns error when first-admin is incomplete
    /// (keycloak-bootstrap not done means first-admin can't be done either).
    /// </summary>
    [Fact]
    public async Task CompleteStep_WhenKeycloakBootstrapIncomplete_ReturnsError()
    {
        // Arrange — app-identity and identity-provider done, but keycloak-bootstrap NOT done
        var settings = Substitute.For<ISettingsService>();

        // App identity completed
        settings.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("My App")));
        settings.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("example.com")));

        // Identity provider completed
        settings.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("https://keycloak.example.com")));
        settings.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("groundup")));

        // Keycloak bootstrap NOT completed
        settings.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));
        settings.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok("some-secret")));

        // Act
        var appIdentityError = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);
        var idpError = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);
        var kcError = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert — app-identity and idp pass, but keycloak fails
        appIdentityError.Should().BeNull();
        idpError.Should().BeNull();
        kcError.Should().NotBeNull();
        kcError.Should().Contain("Keycloak admin client");
    }

    #endregion

    #region Req 16.5: Shared precondition helper consistency

    /// <summary>
    /// Validates Req 16.5: The step ordering check is implemented in a single shared
    /// precondition helper. All methods return null on success and a descriptive error
    /// message on failure — consistent behavior across all wizard endpoints.
    /// </summary>
    [Fact]
    public async Task AllPreconditionChecks_ReturnConsistentErrorFormat()
    {
        // Arrange — all preconditions fail
        var settings = Substitute.For<ISettingsService>();
        settings.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperationResult<string>.Ok(string.Empty)));

        // Act
        var appIdentityError = await SetupPreconditions.CheckAppIdentityAsync(settings, CancellationToken.None);
        var idpError = await SetupPreconditions.CheckIdentityProviderAsync(settings, CancellationToken.None);
        var kcError = await SetupPreconditions.CheckKeycloakBootstrapAsync(settings, CancellationToken.None);

        // Assert — all return non-null error messages
        appIdentityError.Should().NotBeNull();
        idpError.Should().NotBeNull();
        kcError.Should().NotBeNull();

        // Each error names the specific missing prerequisite step
        appIdentityError.Should().Contain("App identity");
        idpError.Should().Contain("Identity provider");
        kcError.Should().Contain("Keycloak admin client");
    }

    #endregion
}
