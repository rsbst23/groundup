namespace GroundUp.Services.Setup;

using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Results;

/// <summary>
/// Orchestrates setup wizard steps. Each method validates preconditions,
/// persists settings via ISettingsService, and writes transaction-log rows
/// for cross-system operations (Keycloak bootstrap, first-admin).
/// </summary>
public interface ISetupWizardService
{
    Task<OperationResult<SetupStatusDto>> GetStatusAsync(CancellationToken ct = default);
    Task<OperationResult<StepResultDto>> SetAppIdentityAsync(SetAppIdentityRequest request, CancellationToken ct = default);
    Task<OperationResult<StepResultDto>> SetIdentityProviderAsync(SetIdentityProviderRequest request, CancellationToken ct = default);
    Task<OperationResult<KeycloakBootstrapResultDto>> BootstrapKeycloakAsync(KeycloakBootstrapRequest request, string? operatorIp, CancellationToken ct = default);
    Task<OperationResult<FirstAdminResultDto>> CreateFirstAdminAsync(CreateFirstAdminRequest request, string? operatorIp, string? correlationId, CancellationToken ct = default);
    Task<OperationResult<StepResultDto>> CompleteSetupAsync(CancellationToken ct = default);
    Task<OperationResult<IReadOnlyList<SetupTransactionLogDto>>> GetTransactionLogAsync(CancellationToken ct = default);
    Task<OperationResult<RecoverResultDto>> RecoverAsync(Guid transactionLogId, CancellationToken ct = default);
}
