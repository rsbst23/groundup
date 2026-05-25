namespace GroundUp.Auth.Core.Abstractions;

using GroundUp.Auth.Core.Dtos;
using GroundUp.Core.Results;

/// <summary>
/// Administrative contract for external identity provider operations.
/// Supports realm CRUD, client CRUD, and user provisioning.
/// Phase 10B implements against Keycloak; 10A defines the contract only.
/// </summary>
public interface IIdentityProviderAdminService
{
    // --- Realm operations ---

    /// <summary>
    /// Creates a new realm in the identity provider.
    /// </summary>
    /// <param name="request">The realm creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created realm DTO.</returns>
    Task<OperationResult<RealmDto>> CreateRealmAsync(
        CreateRealmRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a realm by its name.
    /// </summary>
    /// <param name="realmName">The realm identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The realm DTO or NotFound.</returns>
    Task<OperationResult<RealmDto>> GetRealmAsync(
        string realmName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing realm.
    /// </summary>
    /// <param name="realmName">The realm identifier to update.</param>
    /// <param name="request">The update request with changed fields.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated realm DTO.</returns>
    Task<OperationResult<RealmDto>> UpdateRealmAsync(
        string realmName, UpdateRealmRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a realm from the identity provider.
    /// </summary>
    /// <param name="realmName">The realm identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<OperationResult> DeleteRealmAsync(
        string realmName, CancellationToken cancellationToken = default);

    // --- Client operations (scoped to a realm) ---

    /// <summary>
    /// Creates a new client within a realm.
    /// </summary>
    /// <param name="realmName">The realm to create the client in.</param>
    /// <param name="request">The client creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created client DTO.</returns>
    Task<OperationResult<IdentityProviderClientDto>> CreateClientAsync(
        string realmName, CreateIdentityProviderClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a client by its ID within a realm.
    /// </summary>
    /// <param name="realmName">The realm containing the client.</param>
    /// <param name="clientId">The client identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The client DTO or NotFound.</returns>
    Task<OperationResult<IdentityProviderClientDto>> GetClientAsync(
        string realmName, string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing client within a realm.
    /// </summary>
    /// <param name="realmName">The realm containing the client.</param>
    /// <param name="clientId">The client identifier to update.</param>
    /// <param name="request">The update request with changed fields.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated client DTO.</returns>
    Task<OperationResult<IdentityProviderClientDto>> UpdateClientAsync(
        string realmName, string clientId, UpdateIdentityProviderClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a client from a realm.
    /// </summary>
    /// <param name="realmName">The realm containing the client.</param>
    /// <param name="clientId">The client identifier to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<OperationResult> DeleteClientAsync(
        string realmName, string clientId, CancellationToken cancellationToken = default);

    // --- User provisioning ---

    /// <summary>
    /// Provisions a new user in the identity provider.
    /// </summary>
    /// <param name="realmName">The realm to provision the user in.</param>
    /// <param name="request">The user provisioning request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provisioned user DTO with external user ID.</returns>
    Task<OperationResult<ProvisionedUserDto>> ProvisionUserAsync(
        string realmName, ProvisionUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or resets credentials for an existing user.
    /// </summary>
    /// <param name="realmName">The realm containing the user.</param>
    /// <param name="externalUserId">The identity provider's user identifier.</param>
    /// <param name="credentials">The credentials to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<OperationResult> SetUserCredentialsAsync(
        string realmName, string externalUserId, IdentityProviderUserCredentialsDto credentials,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a user from the identity provider.
    /// </summary>
    /// <param name="realmName">The realm containing the user.</param>
    /// <param name="externalUserId">The identity provider's user identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<OperationResult> DeleteUserAsync(
        string realmName, string externalUserId, CancellationToken cancellationToken = default);
}
