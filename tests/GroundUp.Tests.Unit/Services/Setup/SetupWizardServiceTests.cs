using FluentAssertions;
using GroundUp.Auth.Core;
using GroundUp.Auth.Core.Abstractions;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Configuration;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using GroundUp.Services.Setup;
using GroundUp.Services.Setup.Keycloak;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Setup;

/// <summary>
/// Unit tests for SetupWizardService covering step validation, precondition
/// enforcement, idempotency, and password complexity.
/// Requirements: 9–14, 16
/// </summary>
public sealed class SetupWizardServiceTests : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IBootstrapStateService _bootstrapStateService;
    private readonly IIdentityBootstrapService _identityBootstrapService;
    private readonly IIdentityProviderAdminService _identityProviderAdminService;
    private readonly IOptions<SetupTransactionLogOptions> _transactionLogOptions;
    private readonly IOptions<BootstrapOptions> _bootstrapOptions;
    private readonly KeycloakAdminHttpClient _keycloakClient;
    private readonly SetupWizardTestDbContext _dbContext;
    private readonly SetupWizardService _sut;

    public SetupWizardServiceTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _bootstrapStateService = Substitute.For<IBootstrapStateService>();
        _identityBootstrapService = Substitute.For<IIdentityBootstrapService>();
        _identityProviderAdminService = Substitute.For<IIdentityProviderAdminService>();
        _transactionLogOptions = Options.Create(new SetupTransactionLogOptions { MaxRowCount = 1000 });
        _bootstrapOptions = Options.Create(new BootstrapOptions());
        _keycloakClient = new KeycloakAdminHttpClient(
            new HttpClient(), NullLogger<KeycloakAdminHttpClient>.Instance);

        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<SetupWizardTestDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new SetupWizardTestDbContext(options);

        // Seed system level
        _dbContext.Set<SettingLevel>().Add(new SettingLevel
        {
            Id = Guid.NewGuid(),
            Name = "system",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        _dbContext.SaveChanges();

        // Default: bootstrap not complete
        _bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Default: EnsureDefinitionAsync succeeds
        _settingsService.EnsureDefinitionAsync(
            Arg.Any<EnsureSettingDefinitionRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = callInfo.ArgAt<EnsureSettingDefinitionRequest>(0);
                return Task.FromResult(OperationResult<SettingDefinitionDto>.Ok(
                    new SettingDefinitionDto(
                        Guid.NewGuid(), req.Key, SettingDataType.String, null, null,
                        req.DisplayName, null, null, null, 0, true, false, false,
                        false, false, false, null, null, null, null, null, null,
                        null, null, null, null)));
            });

        // Default: SetAsync succeeds
        _settingsService.SetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<string>(0);
                var value = callInfo.ArgAt<string>(1);
                return Task.FromResult(OperationResult<SettingValueDto>.Ok(
                    new SettingValueDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, value)));
            });

        _sut = new SetupWizardService(
            _settingsService,
            _bootstrapStateService,
            _dbContext,
            _transactionLogOptions,
            _keycloakClient,
            _bootstrapOptions,
            _identityBootstrapService,
            _identityProviderAdminService,
            NullLogger<SetupWizardService>.Instance);
    }

    public void Dispose() => _dbContext.Dispose();

    #region SetAppIdentityAsync — Validation (Requirement 9)

    [Fact]
    public async Task SetAppIdentityAsync_NullApplicationName_ReturnsBadRequest()
    {
        var request = new SetAppIdentityRequest(null, "example.com");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Application name is required");
    }

    [Fact]
    public async Task SetAppIdentityAsync_EmptyApplicationName_ReturnsBadRequest()
    {
        var request = new SetAppIdentityRequest("", "example.com");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SetAppIdentityAsync_WhitespaceOnlyApplicationName_ReturnsBadRequest()
    {
        var request = new SetAppIdentityRequest("   ", "example.com");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SetAppIdentityAsync_ApplicationNameExceeds200Chars_ReturnsBadRequest()
    {
        var longName = new string('A', 201);
        var request = new SetAppIdentityRequest(longName, "example.com");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("200 characters");
    }

    [Fact]
    public async Task SetAppIdentityAsync_InvalidDomainFormat_ReturnsBadRequest()
    {
        var request = new SetAppIdentityRequest("My App", "not a domain!");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("domain");
    }

    [Fact]
    public async Task SetAppIdentityAsync_EmptyDomain_PersistsEmptyString()
    {
        var request = new SetAppIdentityRequest("My App", "");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeTrue();
        result.Data!.Step.Should().Be("app-identity");
        result.Data.Completed.Should().BeTrue();

        await _settingsService.Received(1).SetAsync(
            "auth.application.default-domain", "", Arg.Any<Guid>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAppIdentityAsync_ValidInput_TrimsWhitespace()
    {
        var request = new SetAppIdentityRequest("  My App  ", "  example.com  ");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeTrue();

        await _settingsService.Received(1).SetAsync(
            "app.identity.name", "My App", Arg.Any<Guid>(), null, Arg.Any<CancellationToken>());
        await _settingsService.Received(1).SetAsync(
            "auth.application.default-domain", "example.com", Arg.Any<Guid>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAppIdentityAsync_ValidInput_ReturnsSuccess()
    {
        var request = new SetAppIdentityRequest("My App", "example.com");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeTrue();
        result.Data!.Step.Should().Be("app-identity");
        result.Data.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task SetAppIdentityAsync_Exactly200Chars_Succeeds()
    {
        var name = new string('A', 200);
        var request = new SetAppIdentityRequest(name, "example.com");

        var result = await _sut.SetAppIdentityAsync(request);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region SetIdentityProviderAsync — Validation (Requirement 10)

    [Fact]
    public async Task SetIdentityProviderAsync_AppIdentityNotCompleted_Returns412()
    {
        SetupSettingsNotConfigured("app.identity.name");

        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com", null, "groundup");

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
        result.Message.Should().Contain("App identity");
    }

    [Fact]
    public async Task SetIdentityProviderAsync_NullPublicBaseUrl_ReturnsBadRequest()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest(null, null, "groundup");

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Public base URL is required");
    }

    [Fact]
    public async Task SetIdentityProviderAsync_InvalidPublicBaseUrl_ReturnsBadRequest()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest("not-a-url", null, "groundup");

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("valid absolute URL");
    }

    [Fact]
    public async Task SetIdentityProviderAsync_FtpSchemePublicBaseUrl_ReturnsBadRequest()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest("ftp://keycloak.example.com", null, "groundup");

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SetIdentityProviderAsync_InvalidInternalBaseUrl_ReturnsBadRequest()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com", "not-a-url", "groundup");

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Internal base URL");
    }

    [Fact]
    public async Task SetIdentityProviderAsync_NullSharedRealmName_ReturnsBadRequest()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com", null, null);

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Shared realm name is required");
    }

    [Fact]
    public async Task SetIdentityProviderAsync_RealmNameExceeds128Chars_ReturnsBadRequest()
    {
        SetupAppIdentityCompleted();

        var longRealm = new string('a', 129);
        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com", null, longRealm);

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("128 characters");
    }

    [Fact]
    public async Task SetIdentityProviderAsync_EmptyInternalBaseUrl_DefaultsToPublicBaseUrl()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com", "", "groundup");

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeTrue();

        await _settingsService.Received(1).SetAsync(
            "auth.keycloak.internal-base-url",
            "https://keycloak.example.com",
            Arg.Any<Guid>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetIdentityProviderAsync_ValidInput_ReturnsSuccess()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com",
            "http://keycloak-internal:8080",
            "groundup");

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeTrue();
        result.Data!.Step.Should().Be("identity-provider");
        result.Data.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task SetIdentityProviderAsync_TrimsWhitespace()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest(
            "  https://keycloak.example.com  ",
            "  http://keycloak-internal:8080  ",
            "  groundup  ");

        var result = await _sut.SetIdentityProviderAsync(request);

        result.Success.Should().BeTrue();

        await _settingsService.Received(1).SetAsync(
            "auth.keycloak.public-base-url",
            "https://keycloak.example.com",
            Arg.Any<Guid>(), null, Arg.Any<CancellationToken>());
    }

    #endregion

    #region BootstrapKeycloakAsync — Preconditions (Requirement 11, 16)

    [Fact]
    public async Task BootstrapKeycloakAsync_AppIdentityNotCompleted_Returns412()
    {
        SetupSettingsNotConfigured("app.identity.name");

        var request = new KeycloakBootstrapRequest("admin", "password");

        var result = await _sut.BootstrapKeycloakAsync(request, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task BootstrapKeycloakAsync_IdentityProviderNotCompleted_Returns412()
    {
        SetupAppIdentityCompleted();
        SetupSettingsNotConfigured("auth.keycloak.public-base-url");

        var request = new KeycloakBootstrapRequest("admin", "password");

        var result = await _sut.BootstrapKeycloakAsync(request, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
        result.Message.Should().Contain("Identity provider");
    }

    [Fact]
    public async Task BootstrapKeycloakAsync_NoCredentials_ReturnsBadRequest()
    {
        SetupAppIdentityCompleted();
        SetupIdentityProviderCompleted();

        var request = new KeycloakBootstrapRequest(null, null);

        var result = await _sut.BootstrapKeycloakAsync(request, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("credentials are required");
    }

    [Fact]
    public async Task BootstrapKeycloakAsync_CredentialsFromConfig_WhenRequestBodyEmpty()
    {
        SetupAppIdentityCompleted();
        SetupIdentityProviderCompleted();

        // Configure fallback credentials
        _bootstrapOptions.Value.Keycloak.BootstrapAdminUsername = "config-admin";
        _bootstrapOptions.Value.Keycloak.BootstrapAdminPassword = "config-password";

        var request = new KeycloakBootstrapRequest(null, null);

        // This will fail at the HTTP call level, but we verify credentials were resolved
        var result = await _sut.BootstrapKeycloakAsync(request, null);

        // The call should proceed past credential validation (will fail at HTTP level)
        // Since we can't mock the KeycloakAdminHttpClient (concrete class), we just verify
        // it didn't return the "credentials required" error
        result.Message.Should().NotContain("credentials are required");
    }

    #endregion

    #region CreateFirstAdminAsync — Preconditions (Requirement 12, 16)

    [Fact]
    public async Task CreateFirstAdminAsync_AppIdentityNotCompleted_Returns412()
    {
        SetupSettingsNotConfigured("app.identity.name");

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task CreateFirstAdminAsync_IdentityProviderNotCompleted_Returns412()
    {
        SetupAppIdentityCompleted();
        SetupSettingsNotConfigured("auth.keycloak.public-base-url");

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task CreateFirstAdminAsync_KeycloakBootstrapNotCompleted_Returns412()
    {
        SetupAppIdentityCompleted();
        SetupIdentityProviderCompleted();
        SetupSettingsNotConfigured("auth.keycloak.admin-client-id");

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task CreateFirstAdminAsync_SystemTenantMissing_Returns500()
    {
        SetupAllPreconditionsCompleted();
        _identityBootstrapService.HasSystemTenantAsync(Arg.Any<CancellationToken>())
            .Returns(false);
        _identityBootstrapService.HasSuperAdminRoleAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Message.Should().Contain("System tenant is missing");
    }

    [Fact]
    public async Task CreateFirstAdminAsync_SuperAdminRoleMissing_Returns500()
    {
        SetupAllPreconditionsCompleted();
        _identityBootstrapService.HasSystemTenantAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _identityBootstrapService.HasSuperAdminRoleAsync(Arg.Any<CancellationToken>())
            .Returns(false);

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.Message.Should().Contain("SuperAdmin role is missing");
    }

    #endregion

    #region CreateFirstAdminAsync — Input Validation (Requirement 12)

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    [InlineData("@missing.com")]
    public async Task CreateFirstAdminAsync_InvalidEmail_ReturnsBadRequest(string? email)
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var request = new CreateFirstAdminRequest(email, "Admin", "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("email");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateFirstAdminAsync_InvalidDisplayName_ReturnsBadRequest(string? displayName)
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var request = new CreateFirstAdminRequest("admin@example.com", displayName, "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("Display name");
    }

    [Fact]
    public async Task CreateFirstAdminAsync_DisplayNameExceeds200Chars_ReturnsBadRequest()
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var longName = new string('A', 201);
        var request = new CreateFirstAdminRequest("admin@example.com", longName, "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("200 characters");
    }

    #endregion

    #region CreateFirstAdminAsync — Password Complexity (Requirement 12.9)

    [Fact]
    public async Task CreateFirstAdminAsync_PasswordTooShort_ReturnsBadRequest()
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "Short1!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("at least 12 characters");
    }

    [Fact]
    public async Task CreateFirstAdminAsync_PasswordMissingUppercase_ReturnsBadRequest()
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "password1234!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("uppercase");
    }

    [Fact]
    public async Task CreateFirstAdminAsync_PasswordMissingLowercase_ReturnsBadRequest()
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "PASSWORD1234!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("lowercase");
    }

    [Fact]
    public async Task CreateFirstAdminAsync_PasswordMissingDigit_ReturnsBadRequest()
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "PasswordNoDigit!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("digit");
    }

    [Fact]
    public async Task CreateFirstAdminAsync_PasswordMissingSpecialChar_ReturnsBadRequest()
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "Password12345");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("non-alphanumeric");
    }

    [Theory]
    [InlineData("P@ssw0rd12345!")]       // Standard complex password
    [InlineData("MyP@ss12345 x")]        // Space as special char
    [InlineData("Abcdefgh1234{")]        // Brace as special char
    [InlineData("Test12345678~")]        // Tilde as special char
    public async Task CreateFirstAdminAsync_ValidComplexPassword_PassesValidation(string password)
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();
        SetupNoExistingSuperAdmin();
        SetupIdentityProviderProvisionSuccess();

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", password);

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        // Should pass validation (may fail at Keycloak/DB level but not at validation)
        result.Message.Should().NotContain("uppercase")
            .And.NotContain("lowercase")
            .And.NotContain("digit")
            .And.NotContain("non-alphanumeric")
            .And.NotContain("at least 12 characters");
    }

    #endregion

    #region CreateFirstAdminAsync — Idempotency (Requirement 12.18, 12.19, 12.20)

    [Fact]
    public async Task CreateFirstAdminAsync_SuperAdminAlreadyExistsWithSameAttributes_ReturnsSuccess()
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        var existingUserId = Guid.NewGuid();
        _identityBootstrapService.HasSuperAdminAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _identityBootstrapService.ProvisionFirstSuperAdminAsync(
            Arg.Any<ProvisionFirstSuperAdminRequest>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<BootstrapAdminResultDto>.Ok(
                new BootstrapAdminResultDto(existingUserId, "admin@example.com", AlreadyExisted: true)));

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeTrue();
        result.Data!.UserId.Should().Be(existingUserId);
    }

    [Fact]
    public async Task CreateFirstAdminAsync_SuperAdminExistsWithConflictingAttributes_Returns409()
    {
        SetupAllPreconditionsCompleted();
        SetupSystemInfrastructureExists();

        _identityBootstrapService.HasSuperAdminAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _identityBootstrapService.ProvisionFirstSuperAdminAsync(
            Arg.Any<ProvisionFirstSuperAdminRequest>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<BootstrapAdminResultDto>.Fail(
                "Conflicting attributes", 409, "conflict"));

        var request = new CreateFirstAdminRequest("admin@example.com", "Admin", "P@ssw0rd12345!");

        var result = await _sut.CreateFirstAdminAsync(request, null, null);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    #endregion

    #region CompleteSetupAsync — Preconditions (Requirement 14, 16)

    [Fact]
    public async Task CompleteSetupAsync_AlreadyComplete_Returns409()
    {
        _bootstrapStateService.IsCompleteAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _sut.CompleteSetupAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("already complete");
    }

    [Fact]
    public async Task CompleteSetupAsync_AppIdentityMissing_Returns412()
    {
        SetupSettingsNotConfigured("app.identity.name");

        var result = await _sut.CompleteSetupAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task CompleteSetupAsync_IdentityProviderMissing_Returns412()
    {
        SetupAppIdentityCompleted();
        SetupSettingsNotConfigured("auth.keycloak.public-base-url");

        var result = await _sut.CompleteSetupAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task CompleteSetupAsync_KeycloakBootstrapMissing_Returns412()
    {
        SetupAppIdentityCompleted();
        SetupIdentityProviderCompleted();
        SetupSettingsNotConfigured("auth.keycloak.admin-client-id");

        var result = await _sut.CompleteSetupAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
    }

    [Fact]
    public async Task CompleteSetupAsync_NoSuperAdmin_Returns412()
    {
        SetupAllPreconditionsCompleted();
        _identityBootstrapService.HasSuperAdminAsync(Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _sut.CompleteSetupAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
        result.Message.Should().Contain("super admin must be created");
    }

    [Fact]
    public async Task CompleteSetupAsync_PendingTransactionLog_Returns412()
    {
        SetupAllPreconditionsCompleted();
        _identityBootstrapService.HasSuperAdminAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Add a pending transaction log row
        _dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = Guid.NewGuid(),
            Operation = "first-admin-create",
            Stage = "db-pending",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.CompleteSetupAsync();

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(412);
        result.Message.Should().Contain("pending");
    }

    [Fact]
    public async Task CompleteSetupAsync_AllPreconditionsMet_ReturnsSuccess()
    {
        SetupAllPreconditionsCompleted();
        var superAdminId = Guid.NewGuid();
        _identityBootstrapService.HasSuperAdminAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _identityBootstrapService.GetSuperAdminUserIdAsync(Arg.Any<CancellationToken>())
            .Returns(superAdminId);
        _bootstrapStateService.CompleteSetupAsync(superAdminId, Arg.Any<CancellationToken>())
            .Returns(OperationResult.Ok());

        var result = await _sut.CompleteSetupAsync();

        result.Success.Should().BeTrue();
        result.Data!.Step.Should().Be("complete");
        result.Data.Completed.Should().BeTrue();
    }

    #endregion

    #region SetAppIdentityAsync — Idempotency (Requirement 9.8)

    [Fact]
    public async Task SetAppIdentityAsync_RepeatedCallsSameInput_ProducesSameResult()
    {
        var request = new SetAppIdentityRequest("My App", "example.com");

        var result1 = await _sut.SetAppIdentityAsync(request);
        var result2 = await _sut.SetAppIdentityAsync(request);

        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        result1.Data!.Step.Should().Be(result2.Data!.Step);
        result1.Data.Completed.Should().Be(result2.Data.Completed);
    }

    #endregion

    #region SetIdentityProviderAsync — Idempotency (Requirement 10.10)

    [Fact]
    public async Task SetIdentityProviderAsync_RepeatedCallsSameInput_ProducesSameResult()
    {
        SetupAppIdentityCompleted();

        var request = new SetIdentityProviderRequest(
            "https://keycloak.example.com",
            "http://keycloak-internal:8080",
            "groundup");

        var result1 = await _sut.SetIdentityProviderAsync(request);
        var result2 = await _sut.SetIdentityProviderAsync(request);

        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        result1.Data!.Step.Should().Be(result2.Data!.Step);
    }

    #endregion

    #region RecoverAsync (Requirement 13)

    [Fact]
    public async Task RecoverAsync_NonExistentId_ReturnsNotFound()
    {
        var result = await _sut.RecoverAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task RecoverAsync_CompletedStage_Returns409()
    {
        var logId = Guid.NewGuid();
        _dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = logId,
            Operation = "first-admin-create",
            Stage = "completed",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RecoverAsync(logId);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("already completed");
    }

    [Fact]
    public async Task RecoverAsync_KeycloakPendingStage_Returns409()
    {
        var logId = Guid.NewGuid();
        _dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = logId,
            Operation = "first-admin-create",
            Stage = "keycloak-pending",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RecoverAsync(logId);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("keycloak-pending");
    }

    [Fact]
    public async Task RecoverAsync_DbPendingWithValidData_RecoverSuccessfully()
    {
        var logId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = logId,
            Operation = "first-admin-create",
            Stage = "db-pending",
            ExternalUserId = "ext-user-123",
            Email = "admin@example.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        });
        await _dbContext.SaveChangesAsync();

        _identityBootstrapService.ProvisionFirstSuperAdminAsync(
            Arg.Any<ProvisionFirstSuperAdminRequest>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<BootstrapAdminResultDto>.Ok(
                new BootstrapAdminResultDto(userId, "admin@example.com", AlreadyExisted: false)));

        var result = await _sut.RecoverAsync(logId);

        result.Success.Should().BeTrue();
        result.Data!.UserId.Should().Be(userId);

        // Verify the log was updated to completed
        var updatedLog = await _dbContext.SetupTransactionLogs.FindAsync(logId);
        updatedLog!.Stage.Should().Be("completed");
    }

    [Fact]
    public async Task RecoverAsync_DbPendingMissingExternalUserId_MarksFailed()
    {
        var logId = Guid.NewGuid();
        _dbContext.SetupTransactionLogs.Add(new SetupTransactionLog
        {
            Id = logId,
            Operation = "first-admin-create",
            Stage = "db-pending",
            ExternalUserId = null,
            Email = "admin@example.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "setup-wizard"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.RecoverAsync(logId);

        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(500);

        var updatedLog = await _dbContext.SetupTransactionLogs.FindAsync(logId);
        updatedLog!.Stage.Should().Be("failed");
    }

    #endregion

    #region Helpers

    private void SetupAppIdentityCompleted()
    {
        _settingsService.GetAsync<string>("app.identity.name", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("Test App"));
        _settingsService.GetAsync<string>("auth.application.default-domain", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("example.com"));
    }

    private void SetupIdentityProviderCompleted()
    {
        _settingsService.GetAsync<string>("auth.keycloak.public-base-url", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("https://keycloak.example.com"));
        _settingsService.GetAsync<string>("auth.keycloak.shared-realm-name", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("groundup"));
        _settingsService.GetAsync<string>("auth.keycloak.internal-base-url", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("http://keycloak-internal:8080"));
    }

    private void SetupKeycloakBootstrapCompleted()
    {
        _settingsService.GetAsync<string>("auth.keycloak.admin-client-id", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("groundup-admin-client"));
        _settingsService.GetAsync<string>("auth.keycloak.admin-client-secret", Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Ok("secret-value"));
    }

    private void SetupAllPreconditionsCompleted()
    {
        SetupAppIdentityCompleted();
        SetupIdentityProviderCompleted();
        SetupKeycloakBootstrapCompleted();
    }

    private void SetupSystemInfrastructureExists()
    {
        _identityBootstrapService.HasSystemTenantAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _identityBootstrapService.HasSuperAdminRoleAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _identityBootstrapService.HasSuperAdminAsync(Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private void SetupNoExistingSuperAdmin()
    {
        _identityBootstrapService.HasSuperAdminAsync(Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private void SetupIdentityProviderProvisionSuccess()
    {
        _identityProviderAdminService.ProvisionUserAsync(
            Arg.Any<string>(), Arg.Any<ProvisionUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(OperationResult<ProvisionedUserDto>.Ok(
                new ProvisionedUserDto("ext-user-" + Guid.NewGuid().ToString("N")[..8], "admin@example.com", "Admin", false)));

        _identityBootstrapService.ProvisionFirstSuperAdminAsync(
            Arg.Any<ProvisionFirstSuperAdminRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = callInfo.ArgAt<ProvisionFirstSuperAdminRequest>(0);
                return OperationResult<BootstrapAdminResultDto>.Ok(
                    new BootstrapAdminResultDto(Guid.NewGuid(), req.Email, AlreadyExisted: false));
            });
    }

    private void SetupSettingsNotConfigured(string key)
    {
        _settingsService.GetAsync<string>(key, Arg.Any<CancellationToken>())
            .Returns(OperationResult<string>.Fail($"Setting '{key}' not found.", 404));
    }

    #endregion

    #region Test DbContext

    /// <summary>
    /// Concrete DbContext for SetupWizardService tests.
    /// </summary>
    private sealed class SetupWizardTestDbContext : GroundUpDbContext
    {
        public SetupWizardTestDbContext(DbContextOptions<SetupWizardTestDbContext> options)
            : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SetupTransactionLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Operation).HasMaxLength(100);
                entity.Property(e => e.Stage).HasMaxLength(50);
            });

            modelBuilder.Entity<BootstrapState>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<SettingLevel>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).HasMaxLength(100);
            });
        }
    }

    #endregion
}
