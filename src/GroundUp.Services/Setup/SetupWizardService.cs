namespace GroundUp.Services.Setup;

using System.Net;
using System.Text.RegularExpressions;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Configuration;
using GroundUp.Core.Dtos.Settings;
using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Results;
using GroundUp.Data.Postgres;
using GroundUp.Services.Setup.Keycloak;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Orchestrates setup wizard steps. Covers app-identity, identity-provider,
/// and keycloak-bootstrap steps. Remaining steps (first-admin, complete, status,
/// transaction-log, recover) will be added in subsequent tasks.
/// </summary>
internal sealed class SetupWizardService : ISetupWizardService
{
    private static readonly Regex DomainRegex = new(
        @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private readonly ISettingsService _settingsService;
    private readonly IBootstrapStateService _bootstrapStateService;
    private readonly GroundUpDbContext _dbContext;
    private readonly IOptions<SetupTransactionLogOptions> _transactionLogOptions;
    private readonly KeycloakAdminHttpClient _keycloakClient;
    private readonly IOptions<BootstrapOptions> _bootstrapOptions;
    private readonly ILogger<SetupWizardService> _logger;

    public SetupWizardService(
        ISettingsService settingsService,
        IBootstrapStateService bootstrapStateService,
        GroundUpDbContext dbContext,
        IOptions<SetupTransactionLogOptions> transactionLogOptions,
        KeycloakAdminHttpClient keycloakClient,
        IOptions<BootstrapOptions> bootstrapOptions,
        ILogger<SetupWizardService> logger)
    {
        _settingsService = settingsService;
        _bootstrapStateService = bootstrapStateService;
        _dbContext = dbContext;
        _transactionLogOptions = transactionLogOptions;
        _keycloakClient = keycloakClient;
        _bootstrapOptions = bootstrapOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<StepResultDto>> SetAppIdentityAsync(
        SetAppIdentityRequest request, CancellationToken ct = default)
    {
        // 1. Trim inputs
        var applicationName = request.ApplicationName?.Trim() ?? string.Empty;
        var defaultDomain = request.DefaultDomain?.Trim() ?? string.Empty;

        // 2. Validate applicationName
        if (string.IsNullOrEmpty(applicationName))
            return OperationResult<StepResultDto>.BadRequest("Application name is required.");

        if (applicationName.Length > 200)
            return OperationResult<StepResultDto>.BadRequest("Application name must not exceed 200 characters.");

        // 3. Validate defaultDomain (only if non-empty)
        if (!string.IsNullOrEmpty(defaultDomain))
        {
            var domainLower = defaultDomain.ToLowerInvariant();
            if (!DomainRegex.IsMatch(domainLower))
                return OperationResult<StepResultDto>.BadRequest("Default domain format is invalid.");
        }

        // 4. Ensure setting definitions exist
        await EnsureAppIdentityDefinitionsAsync(ct);

        // 5. Persist values
        var systemLevelId = await GetSystemLevelIdAsync(ct);

        var nameResult = await _settingsService.SetAsync(
            "app.identity.name", applicationName, systemLevelId, null, ct);
        if (!nameResult.Success)
            return OperationResult<StepResultDto>.Fail(nameResult.Message, nameResult.StatusCode);

        var domainResult = await _settingsService.SetAsync(
            "auth.application.default-domain", defaultDomain, systemLevelId, null, ct);
        if (!domainResult.Success)
            return OperationResult<StepResultDto>.Fail(domainResult.Message, domainResult.StatusCode);

        _logger.LogInformation("App identity step completed: name={ApplicationName}, domain={DefaultDomain}",
            applicationName, defaultDomain);

        return OperationResult<StepResultDto>.Ok(new StepResultDto("app-identity", true));
    }

    /// <inheritdoc />
    public async Task<OperationResult<StepResultDto>> SetIdentityProviderAsync(
        SetIdentityProviderRequest request, CancellationToken ct = default)
    {
        // 1. Precondition check — app-identity must be completed first
        var preconditionError = await SetupPreconditions.CheckAppIdentityAsync(_settingsService, ct);
        if (preconditionError is not null)
            return OperationResult<StepResultDto>.Fail(preconditionError, 412, "precondition_step_missing");

        // 2. Trim inputs
        var publicBaseUrl = request.PublicBaseUrl?.Trim() ?? string.Empty;
        var internalBaseUrl = request.InternalBaseUrl?.Trim() ?? string.Empty;
        var sharedRealmName = request.SharedRealmName?.Trim() ?? string.Empty;

        // 3. Validate publicBaseUrl
        if (string.IsNullOrEmpty(publicBaseUrl))
            return OperationResult<StepResultDto>.BadRequest("Public base URL is required.");

        if (!IsValidAbsoluteHttpUrl(publicBaseUrl))
            return OperationResult<StepResultDto>.BadRequest(
                "Public base URL must be a valid absolute URL with http or https scheme.");

        // 4. Validate internalBaseUrl (only if non-empty)
        if (!string.IsNullOrEmpty(internalBaseUrl) && !IsValidAbsoluteHttpUrl(internalBaseUrl))
            return OperationResult<StepResultDto>.BadRequest(
                "Internal base URL must be a valid absolute URL with http or https scheme.");

        // 5. Validate sharedRealmName
        if (string.IsNullOrEmpty(sharedRealmName))
            return OperationResult<StepResultDto>.BadRequest("Shared realm name is required.");

        if (sharedRealmName.Length > 128)
            return OperationResult<StepResultDto>.BadRequest("Shared realm name must not exceed 128 characters.");

        // 6. Default internalBaseUrl to publicBaseUrl when empty (per Req 10.6)
        if (string.IsNullOrEmpty(internalBaseUrl))
            internalBaseUrl = publicBaseUrl;

        // 7. Ensure setting definitions exist
        await EnsureIdentityProviderDefinitionsAsync(ct);

        // 8. Persist values
        var systemLevelId = await GetSystemLevelIdAsync(ct);

        var publicResult = await _settingsService.SetAsync(
            "auth.keycloak.public-base-url", publicBaseUrl, systemLevelId, null, ct);
        if (!publicResult.Success)
            return OperationResult<StepResultDto>.Fail(publicResult.Message, publicResult.StatusCode);

        var internalResult = await _settingsService.SetAsync(
            "auth.keycloak.internal-base-url", internalBaseUrl, systemLevelId, null, ct);
        if (!internalResult.Success)
            return OperationResult<StepResultDto>.Fail(internalResult.Message, internalResult.StatusCode);

        var realmResult = await _settingsService.SetAsync(
            "auth.keycloak.shared-realm-name", sharedRealmName, systemLevelId, null, ct);
        if (!realmResult.Success)
            return OperationResult<StepResultDto>.Fail(realmResult.Message, realmResult.StatusCode);

        _logger.LogInformation(
            "Identity provider step completed: publicUrl={PublicBaseUrl}, internalUrl={InternalBaseUrl}, realm={SharedRealmName}",
            publicBaseUrl, internalBaseUrl, sharedRealmName);

        return OperationResult<StepResultDto>.Ok(new StepResultDto("identity-provider", true));
    }

    /// <inheritdoc />
    public async Task<OperationResult<KeycloakBootstrapResultDto>> BootstrapKeycloakAsync(
        KeycloakBootstrapRequest request, string? operatorIp, CancellationToken ct = default)
    {
        // 1. Precondition checks — app-identity AND identity-provider must be completed
        var appIdentityError = await SetupPreconditions.CheckAppIdentityAsync(_settingsService, ct);
        if (appIdentityError is not null)
            return OperationResult<KeycloakBootstrapResultDto>.Fail(appIdentityError, 412, "precondition_step_missing");

        var idpError = await SetupPreconditions.CheckIdentityProviderAsync(_settingsService, ct);
        if (idpError is not null)
            return OperationResult<KeycloakBootstrapResultDto>.Fail(idpError, 412, "precondition_step_missing");

        // 2. Resolve credentials from request body OR config fallback
        var username = request.MasterAdminUsername?.Trim();
        var password = request.MasterAdminPassword?.Trim();

        if (string.IsNullOrEmpty(username))
            username = _bootstrapOptions.Value.Keycloak.BootstrapAdminUsername;
        if (string.IsNullOrEmpty(password))
            password = _bootstrapOptions.Value.Keycloak.BootstrapAdminPassword;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return OperationResult<KeycloakBootstrapResultDto>.BadRequest(
                "Keycloak master admin credentials are required for bootstrap.");

        // 3. Read settings for base URL and realm
        var baseUrlResult = await _settingsService.GetAsync<string>("auth.keycloak.public-base-url", ct);
        var baseUrl = baseUrlResult.Data!;

        var realmResult = await _settingsService.GetAsync<string>("auth.keycloak.shared-realm-name", ct);
        var realm = realmResult.Data!;

        try
        {
            // 4. Acquire admin token
            var tokenResponse = await _keycloakClient.AcquireAdminTokenAsync(baseUrl, username, password, ct);
            var accessToken = tokenResponse.AccessToken;

            // 5. Check if client already exists
            const string adminClientId = "groundup-admin-client";
            var existingClient = await _keycloakClient.GetClientByClientIdAsync(
                baseUrl, realm, accessToken, adminClientId, ct);

            KeycloakClientRepresentation client;
            if (existingClient is not null)
            {
                _logger.LogDebug("Client {ClientId} already exists in realm {Realm}; reusing", adminClientId, realm);
                client = existingClient;
            }
            else
            {
                _logger.LogInformation("Creating service-account client {ClientId} in realm {Realm}", adminClientId, realm);
                client = await _keycloakClient.CreateServiceAccountClientAsync(
                    baseUrl, realm, accessToken, adminClientId, ct);
            }

            // 6. Validate and fix role mappings (idempotent — always re-check)
            var currentRoles = await _keycloakClient.GetServiceAccountRolesAsync(
                baseUrl, realm, accessToken, client.Id, ct);

            var missingRoles = KeycloakAdminHttpClient.RequiredRealmManagementRoles
                .Except(currentRoles, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingRoles.Count > 0)
            {
                _logger.LogInformation("Adding {Count} missing realm-management roles to {ClientId}: {Roles}",
                    missingRoles.Count, adminClientId, string.Join(", ", missingRoles));
                await _keycloakClient.AddServiceAccountRolesAsync(
                    baseUrl, realm, accessToken, client.Id, missingRoles, ct);
            }

            // 7. Retrieve client secret
            var clientSecret = await _keycloakClient.GetClientSecretAsync(
                baseUrl, realm, accessToken, client.Id, ct);

            // 8. Ensure setting definitions and persist values
            await EnsureKeycloakBootstrapDefinitionsAsync(ct);

            var systemLevelId = await GetSystemLevelIdAsync(ct);

            var clientIdResult = await _settingsService.SetAsync(
                "auth.keycloak.admin-client-id", adminClientId, systemLevelId, null, ct);
            if (!clientIdResult.Success)
                return OperationResult<KeycloakBootstrapResultDto>.Fail(clientIdResult.Message, clientIdResult.StatusCode);

            var secretSetResult = await _settingsService.SetAsync(
                "auth.keycloak.admin-client-secret", clientSecret, systemLevelId, null, ct);
            if (!secretSetResult.Success)
                return OperationResult<KeycloakBootstrapResultDto>.Fail(secretSetResult.Message, secretSetResult.StatusCode);

            _logger.LogInformation("Keycloak bootstrap step completed: clientId={ClientId}", adminClientId);

            return OperationResult<KeycloakBootstrapResultDto>.Ok(
                new KeycloakBootstrapResultDto("keycloak-bootstrap", true, adminClientId));
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Keycloak master admin credentials were rejected (HTTP {StatusCode})", (int?)ex.StatusCode);
            return OperationResult<KeycloakBootstrapResultDto>.Fail(
                "Keycloak master admin credentials were rejected by Keycloak.", 400, "keycloak_credentials_rejected");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Keycloak responded with an error during bootstrap (HTTP {StatusCode})", (int?)ex.StatusCode);
            return OperationResult<KeycloakBootstrapResultDto>.Fail(
                "Keycloak responded with an error during bootstrap.", 502, "keycloak_error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error during Keycloak bootstrap");
            return OperationResult<KeycloakBootstrapResultDto>.Fail(
                "Keycloak responded with an error during bootstrap.", 502, "keycloak_error");
        }
        finally
        {
            // Scrub credentials from memory
            if (password is not null)
            {
                var pwBytes = System.Text.Encoding.UTF8.GetBytes(password);
                Array.Clear(pwBytes, 0, pwBytes.Length);
            }
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<FirstAdminResultDto>> CreateFirstAdminAsync(
        CreateFirstAdminRequest request, string? operatorIp, string? correlationId, CancellationToken ct = default)
    {
        // Will be implemented in Task 21 when IIdentityProviderAdminService and IIdentityBootstrapService are available
        throw new NotImplementedException("CreateFirstAdminAsync will be implemented in a subsequent task.");
    }

    /// <inheritdoc />
    public Task<OperationResult<StepResultDto>> CompleteSetupAsync(CancellationToken ct = default)
    {
        // Will be implemented in a subsequent task
        throw new NotImplementedException("CompleteSetupAsync will be implemented in a subsequent task.");
    }

    /// <inheritdoc />
    public Task<OperationResult<SetupStatusDto>> GetStatusAsync(CancellationToken ct = default)
    {
        // Will be implemented in a subsequent task
        throw new NotImplementedException("GetStatusAsync will be implemented in a subsequent task.");
    }

    /// <inheritdoc />
    public Task<OperationResult<IReadOnlyList<SetupTransactionLogDto>>> GetTransactionLogAsync(CancellationToken ct = default)
    {
        // Will be implemented in a subsequent task
        throw new NotImplementedException("GetTransactionLogAsync will be implemented in a subsequent task.");
    }

    /// <inheritdoc />
    public Task<OperationResult<RecoverResultDto>> RecoverAsync(Guid transactionLogId, CancellationToken ct = default)
    {
        // Will be implemented in a subsequent task
        throw new NotImplementedException("RecoverAsync will be implemented in a subsequent task.");
    }

    // ─── Private Helpers ───────────────────────────────────────────────────────

    private async Task<Guid> GetSystemLevelIdAsync(CancellationToken ct)
    {
        var level = await _dbContext.Set<SettingLevel>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == "system", ct);
        return level?.Id ?? throw new InvalidOperationException("System setting level not found.");
    }

    private static bool IsValidAbsoluteHttpUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == "http" || uri.Scheme == "https");
    }

    private async Task EnsureAppIdentityDefinitionsAsync(CancellationToken ct)
    {
        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "app.identity.name",
            DataType: SettingDataType.String,
            DefaultValue: null!,
            DisplayName: "Application Name",
            Description: "The display name of the application.",
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            IsRequired: true,
            IsSecret: false,
            IsEncrypted: false), ct);

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.application.default-domain",
            DataType: SettingDataType.String,
            DefaultValue: null!,
            DisplayName: "Default Domain",
            Description: "The default domain for the application (e.g., example.com). Empty for host-only cookies.",
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            RegexPattern: @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$",
            IsRequired: false,
            IsSecret: false,
            IsEncrypted: false), ct);
    }

    private async Task EnsureIdentityProviderDefinitionsAsync(CancellationToken ct)
    {
        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.public-base-url",
            DataType: SettingDataType.String,
            DefaultValue: null!,
            DisplayName: "Keycloak Public Base URL",
            Description: "The public-facing Keycloak base URL used by browsers.",
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            IsRequired: true,
            IsSecret: false,
            IsEncrypted: false), ct);

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.internal-base-url",
            DataType: SettingDataType.String,
            DefaultValue: null!,
            DisplayName: "Keycloak Internal Base URL",
            Description: "The internal Keycloak base URL used by backend services. Defaults to public URL if empty.",
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            IsRequired: false,
            IsSecret: false,
            IsEncrypted: false), ct);

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.shared-realm-name",
            DataType: SettingDataType.String,
            DefaultValue: "groundup",
            DisplayName: "Shared Realm Name",
            Description: "The Keycloak realm name shared across tenants.",
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            IsRequired: true,
            IsSecret: false,
            IsEncrypted: false), ct);
    }

    private async Task EnsureKeycloakBootstrapDefinitionsAsync(CancellationToken ct)
    {
        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.admin-client-id",
            DataType: SettingDataType.String,
            DefaultValue: null!,
            DisplayName: "Keycloak Admin Client ID",
            Description: "The clientId of the Keycloak service-account client used for admin API access.",
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            IsRequired: true,
            IsSecret: false,
            IsEncrypted: false), ct);

        await _settingsService.EnsureDefinitionAsync(new EnsureSettingDefinitionRequest(
            Key: "auth.keycloak.admin-client-secret",
            DataType: SettingDataType.String,
            DefaultValue: null!,
            DisplayName: "Keycloak Admin Client Secret",
            Description: "The client secret for the Keycloak service-account client. Encrypted at rest.",
            Category: null,
            GroupKey: "groundup.setup",
            GroupDisplayName: "Setup",
            AllowedLevelNames: new[] { "system" },
            IsRequired: true,
            IsSecret: true,
            IsEncrypted: true), ct);
    }
}
