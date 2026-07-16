namespace GroundUp.Services.Setup;

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
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
using GroundUp.Services.Setup.Keycloak;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Orchestrates setup wizard steps. Covers app-identity, identity-provider,
/// keycloak-bootstrap, first-admin, and recovery steps.
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
    private readonly IIdentityBootstrapService _identityBootstrapService;
    private readonly IIdentityProviderAdminService _identityProviderAdminService;
    private readonly ILogger<SetupWizardService> _logger;

    public SetupWizardService(
        ISettingsService settingsService,
        IBootstrapStateService bootstrapStateService,
        GroundUpDbContext dbContext,
        IOptions<SetupTransactionLogOptions> transactionLogOptions,
        KeycloakAdminHttpClient keycloakClient,
        IOptions<BootstrapOptions> bootstrapOptions,
        IIdentityBootstrapService identityBootstrapService,
        IIdentityProviderAdminService identityProviderAdminService,
        ILogger<SetupWizardService> logger)
    {
        _settingsService = settingsService;
        _bootstrapStateService = bootstrapStateService;
        _dbContext = dbContext;
        _transactionLogOptions = transactionLogOptions;
        _keycloakClient = keycloakClient;
        _bootstrapOptions = bootstrapOptions;
        _identityBootstrapService = identityBootstrapService;
        _identityProviderAdminService = identityProviderAdminService;
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
    public async Task<OperationResult<FirstAdminResultDto>> CreateFirstAdminAsync(
        CreateFirstAdminRequest request, string? operatorIp, string? correlationId, CancellationToken ct = default)
    {
        // 1. Precondition checks (412 if any fail)
        var appIdentityError = await SetupPreconditions.CheckAppIdentityAsync(_settingsService, ct);
        if (appIdentityError is not null)
            return OperationResult<FirstAdminResultDto>.Fail(appIdentityError, 412, "precondition_step_missing");

        var idpError = await SetupPreconditions.CheckIdentityProviderAsync(_settingsService, ct);
        if (idpError is not null)
            return OperationResult<FirstAdminResultDto>.Fail(idpError, 412, "precondition_step_missing");

        var kcError = await SetupPreconditions.CheckKeycloakBootstrapAsync(_settingsService, ct);
        if (kcError is not null)
            return OperationResult<FirstAdminResultDto>.Fail(kcError, 412, "precondition_step_missing");

        // System tenant and SuperAdmin role must exist
        if (!await _identityBootstrapService.HasSystemTenantAsync(ct))
            return OperationResult<FirstAdminResultDto>.Fail(
                "System tenant is missing — verify the framework startup seeders have run successfully.", 500);

        if (!await _identityBootstrapService.HasSuperAdminRoleAsync(ct))
            return OperationResult<FirstAdminResultDto>.Fail(
                "SuperAdmin role is missing — verify the auth seeders have run.", 500);

        // 2. Input validation
        var email = request.Email?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        if (string.IsNullOrEmpty(email) || !IsValidEmail(email))
            return OperationResult<FirstAdminResultDto>.BadRequest(
                "A valid email address is required.");

        if (string.IsNullOrWhiteSpace(displayName))
            return OperationResult<FirstAdminResultDto>.BadRequest(
                "Display name is required.");

        if (displayName.Length > 200)
            return OperationResult<FirstAdminResultDto>.BadRequest(
                "Display name must not exceed 200 characters.");

        if (password.Length < 12)
            return OperationResult<FirstAdminResultDto>.BadRequest(
                "Password must be at least 12 characters.");

        if (!HasPasswordComplexity(password))
            return OperationResult<FirstAdminResultDto>.BadRequest(
                "Password must contain at least one uppercase letter, one lowercase letter, one digit, and one non-alphanumeric character.");

        // 3. Idempotency check
        if (await _identityBootstrapService.HasSuperAdminAsync(ct))
        {
            // A super admin already exists — check if it matches
            var existingResult = await _identityBootstrapService.ProvisionFirstSuperAdminAsync(
                new ProvisionFirstSuperAdminRequest(email, displayName, string.Empty, AuthRoleNames.SystemTenantId), ct);

            if (existingResult.Success && existingResult.Data!.AlreadyExisted)
            {
                return OperationResult<FirstAdminResultDto>.Ok(
                    new FirstAdminResultDto("first-admin", true, existingResult.Data.UserId, existingResult.Data.Email));
            }

            if (existingResult.StatusCode == 409)
            {
                return OperationResult<FirstAdminResultDto>.Fail(
                    "A super admin user already exists with conflicting attributes.", 409, "conflict");
            }
        }

        // 4. Read realm name from settings
        var realmResult = await _settingsService.GetAsync<string>("auth.keycloak.shared-realm-name", ct);
        if (!realmResult.Success || string.IsNullOrEmpty(realmResult.Data))
            return OperationResult<FirstAdminResultDto>.Fail(
                "Shared realm name setting is missing.", 500);

        var realmName = realmResult.Data;

        // 5. Transaction log: insert keycloak-pending
        var txLog = new SetupTransactionLog
        {
            Operation = "first-admin-create",
            Stage = "keycloak-pending",
            Email = email,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString()
        };
        await InsertWithRotationAsync(txLog, ct);

        // 6. Keycloak user provisioning
        string externalUserId;
        try
        {
            var provisionResult = await _identityProviderAdminService.ProvisionUserAsync(
                realmName,
                new ProvisionUserRequest(email, displayName, password, RequirePasswordReset: false),
                ct);

            if (provisionResult.Success)
            {
                externalUserId = provisionResult.Data!.ExternalUserId;
            }
            else if (provisionResult.StatusCode == 409)
            {
                // User already exists in Keycloak — check if display name matches
                // The IIdentityProviderAdminService should return the existing user info on 409
                _logger.LogWarning(
                    "Keycloak user with email {Email} already exists. Message: {Message}",
                    email, provisionResult.Message);

                return OperationResult<FirstAdminResultDto>.Fail(
                    "A different user already exists with that email in Keycloak; resolve manually before retrying.",
                    409, "conflict");
            }
            else if (provisionResult.StatusCode == 401 || provisionResult.StatusCode == 403)
            {
                return OperationResult<FirstAdminResultDto>.BadRequest(
                    "Keycloak credentials rejected.");
            }
            else
            {
                _logger.LogError(
                    "Keycloak user provisioning failed with status {StatusCode}: {Message}",
                    provisionResult.StatusCode, provisionResult.Message);

                return OperationResult<FirstAdminResultDto>.Fail(
                    "Keycloak responded with an error.", 502, "keycloak_error");
            }
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == HttpStatusCode.Unauthorized || ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError(ex, "Keycloak credentials rejected during first-admin provisioning");
            return OperationResult<FirstAdminResultDto>.BadRequest("Keycloak credentials rejected.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Keycloak error during first-admin provisioning (HTTP {StatusCode})", (int?)ex.StatusCode);
            return OperationResult<FirstAdminResultDto>.Fail(
                "Keycloak responded with an error.", 502, "keycloak_error");
        }

        // 7. Update transaction log: db-pending
        txLog.Stage = "db-pending";
        txLog.ExternalUserId = externalUserId;
        await _dbContext.SaveChangesAsync(ct);

        // 8. DB provisioning
        try
        {
            var dbResult = await _identityBootstrapService.ProvisionFirstSuperAdminAsync(
                new ProvisionFirstSuperAdminRequest(email, displayName, externalUserId, AuthRoleNames.SystemTenantId),
                ct);

            if (!dbResult.Success)
            {
                _logger.LogError(
                    "Database provisioning failed for first admin {Email}: {Message}",
                    email, dbResult.Message);

                return OperationResult<FirstAdminResultDto>.Fail(
                    "Failed to complete super admin creation; partial state recorded for recovery.", 500);
            }

            // 9. Update transaction log: completed
            txLog.Stage = "completed";
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation(
                "First admin step completed: email={Email}, userId={UserId}",
                email, dbResult.Data!.UserId);

            return OperationResult<FirstAdminResultDto>.Ok(
                new FirstAdminResultDto("first-admin", true, dbResult.Data.UserId, email));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Database provisioning failed for first admin {Email}", email);

            // Leave log at db-pending for recovery
            return OperationResult<FirstAdminResultDto>.Fail(
                "Failed to complete super admin creation; partial state recorded for recovery.", 500);
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<StepResultDto>> CompleteSetupAsync(CancellationToken ct = default)
    {
        // 1. Check if already complete
        var isComplete = await _bootstrapStateService.IsCompleteAsync(ct);
        if (isComplete)
            return OperationResult<StepResultDto>.Fail("Setup is already complete.", 409, "conflict");

        // 2. Precondition checks — all steps must pass
        var appIdentityError = await SetupPreconditions.CheckAppIdentityAsync(_settingsService, ct);
        if (appIdentityError is not null)
            return OperationResult<StepResultDto>.Fail(appIdentityError, 412, "precondition_step_missing");

        var idpError = await SetupPreconditions.CheckIdentityProviderAsync(_settingsService, ct);
        if (idpError is not null)
            return OperationResult<StepResultDto>.Fail(idpError, 412, "precondition_step_missing");

        var kcError = await SetupPreconditions.CheckKeycloakBootstrapAsync(_settingsService, ct);
        if (kcError is not null)
            return OperationResult<StepResultDto>.Fail(kcError, 412, "precondition_step_missing");

        if (!await _identityBootstrapService.HasSuperAdminAsync(ct))
            return OperationResult<StepResultDto>.Fail(
                "First super admin must be created before completing setup.", 412, "precondition_step_missing");

        // Check for pending transaction log rows
        var hasPending = await _dbContext.SetupTransactionLogs
            .AnyAsync(l => l.Operation == "first-admin-create"
                        && (l.Stage == "keycloak-pending" || l.Stage == "db-pending" || l.Stage == "failed"), ct);

        if (hasPending)
            return OperationResult<StepResultDto>.Fail(
                "First admin creation has unresolved pending state. Call /setup/recover/{id} first.",
                412, "precondition_step_missing");

        // 3. Get the super admin user ID
        var superAdminUserId = await _identityBootstrapService.GetSuperAdminUserIdAsync(ct);
        if (superAdminUserId is null)
            return OperationResult<StepResultDto>.Fail(
                "First super admin must be created before completing setup.", 412, "precondition_step_missing");

        // 4. Complete setup
        var completeResult = await _bootstrapStateService.CompleteSetupAsync(superAdminUserId.Value, ct);
        if (!completeResult.Success)
            return OperationResult<StepResultDto>.Fail(completeResult.Message, 409, "conflict");

        _logger.LogInformation("Setup completed by user {UserId}", superAdminUserId.Value);

        // 5. Return success
        return OperationResult<StepResultDto>.Ok(new StepResultDto("complete", true));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SetupStatusDto>> GetStatusAsync(CancellationToken ct = default)
    {
        var isComplete = await _bootstrapStateService.IsCompleteAsync(ct);

        // Check each step's completion status
        var appIdentityError = await SetupPreconditions.CheckAppIdentityAsync(_settingsService, ct);
        var appIdentityCompleted = appIdentityError is null;

        var idpError = await SetupPreconditions.CheckIdentityProviderAsync(_settingsService, ct);
        var identityProviderCompleted = idpError is null;

        var kcError = await SetupPreconditions.CheckKeycloakBootstrapAsync(_settingsService, ct);
        var keycloakBootstrapCompleted = kcError is null;

        var hasSuperAdmin = await _identityBootstrapService.HasSuperAdminAsync(ct);

        // Check for pending transaction log rows
        var pendingRow = await _dbContext.SetupTransactionLogs
            .Where(l => l.Operation == "first-admin-create"
                     && (l.Stage == "keycloak-pending" || l.Stage == "db-pending" || l.Stage == "failed"))
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var firstAdminCompleted = hasSuperAdmin && pendingRow is null;
        var firstAdminPending = pendingRow is not null;
        var firstAdminPendingTransactionLogId = pendingRow?.Id.ToString();

        // Determine current step
        var currentStep = isComplete ? "done"
            : !appIdentityCompleted ? "app-identity"
            : !identityProviderCompleted ? "identity-provider"
            : !keycloakBootstrapCompleted ? "keycloak-bootstrap"
            : !firstAdminCompleted ? "first-admin"
            : "complete";

        return OperationResult<SetupStatusDto>.Ok(new SetupStatusDto(
            isComplete, currentStep,
            appIdentityCompleted, identityProviderCompleted, keycloakBootstrapCompleted,
            firstAdminCompleted, firstAdminPending, firstAdminPendingTransactionLogId));
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SetupTransactionLogDto>>> GetTransactionLogAsync(CancellationToken ct = default)
    {
        var rows = await _dbContext.SetupTransactionLogs
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .Take(20)
            .Select(l => new SetupTransactionLogDto(
                l.Id, l.Operation, l.Stage, l.CorrelationId,
                l.ExternalUserId, l.Email, l.ErrorMessage, l.CreatedAt))
            .ToListAsync(ct);

        return OperationResult<IReadOnlyList<SetupTransactionLogDto>>.Ok(rows);
    }

    /// <inheritdoc />
    public async Task<OperationResult<RecoverResultDto>> RecoverAsync(Guid transactionLogId, CancellationToken ct = default)
    {
        // 1. Find the transaction log row
        var logEntry = await _dbContext.SetupTransactionLogs
            .FirstOrDefaultAsync(l => l.Id == transactionLogId, ct);

        if (logEntry is null)
            return OperationResult<RecoverResultDto>.NotFound("Transaction log entry not found.");

        // 2. Check stage
        if (logEntry.Stage == "completed")
            return OperationResult<RecoverResultDto>.Fail(
                "Transaction log entry is already completed.", 409, "conflict");

        if (logEntry.Stage == "keycloak-pending")
            return OperationResult<RecoverResultDto>.Fail(
                "Cannot auto-recover from keycloak-pending state; the partial Keycloak resource (if any) must be cleaned up manually before retrying the original step.",
                409, "conflict");

        // 3. Handle db-pending recovery for first-admin-create
        if (logEntry.Stage == "db-pending" && logEntry.Operation == "first-admin-create")
        {
            if (string.IsNullOrEmpty(logEntry.ExternalUserId) || string.IsNullOrEmpty(logEntry.Email))
            {
                logEntry.Stage = "failed";
                logEntry.ErrorMessage = "Transaction log entry is missing required data (ExternalUserId or Email).";
                await _dbContext.SaveChangesAsync(ct);

                return OperationResult<RecoverResultDto>.Fail(
                    "Transaction log entry is missing required data for recovery.", 500);
            }

            try
            {
                var dbResult = await _identityBootstrapService.ProvisionFirstSuperAdminAsync(
                    new ProvisionFirstSuperAdminRequest(
                        logEntry.Email,
                        logEntry.Email, // Use email as display name fallback for recovery
                        logEntry.ExternalUserId,
                        AuthRoleNames.SystemTenantId),
                    ct);

                if (dbResult.Success)
                {
                    logEntry.Stage = "completed";
                    await _dbContext.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "Recovery completed for transaction log {Id}: userId={UserId}",
                        transactionLogId, dbResult.Data!.UserId);

                    return OperationResult<RecoverResultDto>.Ok(
                        new RecoverResultDto(true, dbResult.Data.UserId));
                }

                // DB provisioning failed
                logEntry.Stage = "failed";
                logEntry.ErrorMessage = dbResult.Message;
                await _dbContext.SaveChangesAsync(ct);

                return OperationResult<RecoverResultDto>.Fail(
                    $"Recovery failed: {dbResult.Message}", 500);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Recovery failed for transaction log {Id}", transactionLogId);

                logEntry.Stage = "failed";
                logEntry.ErrorMessage = ex.Message;
                await _dbContext.SaveChangesAsync(ct);

                return OperationResult<RecoverResultDto>.Fail(
                    "Recovery failed due to an unexpected error.", 500);
            }
        }

        // Unknown stage/operation combination
        return OperationResult<RecoverResultDto>.Fail(
            $"Cannot recover transaction log entry with stage '{logEntry.Stage}' and operation '{logEntry.Operation}'.",
            409, "conflict");
    }

    // ─── Private Helpers ───────────────────────────────────────────────────────

    private async Task InsertWithRotationAsync(SetupTransactionLog row, CancellationToken ct)
    {
        var max = _transactionLogOptions.Value.MaxRowCount;
        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

        if (max > 0)
        {
            var total = await _dbContext.SetupTransactionLogs.CountAsync(ct);
            var toDelete = (total + 1) - max;
            if (toDelete > 0)
            {
                var deletable = await _dbContext.SetupTransactionLogs
                    .Where(l => l.Stage != "keycloak-pending" && l.Stage != "db-pending")
                    .OrderBy(l => l.CreatedAt)
                    .Take(toDelete)
                    .ToListAsync(ct);

                if (deletable.Count < toDelete)
                {
                    _logger.LogWarning(
                        "Transaction log rotation could not free enough rows: total={Total}, max={Max}, pending protected.",
                        total, max);
                }

                _dbContext.SetupTransactionLogs.RemoveRange(deletable);
            }
        }

        _dbContext.SetupTransactionLogs.Add(row);
        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            // Use System.Net.Mail.MailAddress for RFC-5322 validation
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasPasswordComplexity(string password)
    {
        bool upper = false, lower = false, digit = false, nonAlnum = false;
        foreach (var ch in password)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            switch (cat)
            {
                case UnicodeCategory.UppercaseLetter:
                    upper = true;
                    break;
                case UnicodeCategory.LowercaseLetter:
                    lower = true;
                    break;
                case UnicodeCategory.DecimalDigitNumber:
                    digit = true;
                    break;
                default:
                    // Non-alphanumeric: anything not in Lu, Ll, Lt, Lm, Lo, Nd, Nl, No
                    if (cat is not (UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
                        UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
                        UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or
                        UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber))
                        nonAlnum = true;
                    break;
            }
        }
        return upper && lower && digit && nonAlnum;
    }

    private async Task<Guid> GetSystemLevelIdAsync(CancellationToken ct)
    {
        var level = await _dbContext.Set<SettingLevel>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.Name == "System", ct);
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
