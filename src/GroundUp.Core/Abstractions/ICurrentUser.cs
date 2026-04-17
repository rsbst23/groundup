namespace GroundUp.Core.Abstractions;

/// <summary>
/// Provides the authenticated user's identity to all layers
/// without depending on the authentication module.
/// Implementations are registered as scoped services and populated
/// from JWT claims or other identity sources.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// The authenticated user's unique identifier.
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// The authenticated user's email address, if available.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// The authenticated user's display name, if available.
    /// </summary>
    string? DisplayName { get; }
}
