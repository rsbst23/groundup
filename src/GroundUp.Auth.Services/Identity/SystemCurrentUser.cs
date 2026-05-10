using GroundUp.Core.Abstractions;

namespace GroundUp.Auth.Services.Identity;

/// <summary>
/// Manually-constructed implementation of <see cref="ICurrentUser"/> for non-HTTP scenarios
/// such as background jobs, SDK usage, or integration tests where no HTTP context is available.
/// </summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    /// <summary>
    /// Initializes a new instance of <see cref="SystemCurrentUser"/> with explicit identity values.
    /// </summary>
    /// <param name="userId">The user's unique identifier.</param>
    /// <param name="email">The user's email address, or null if not available.</param>
    /// <param name="displayName">The user's display name, or null if not available.</param>
    public SystemCurrentUser(Guid userId, string? email = null, string? displayName = null)
    {
        UserId = userId;
        Email = email;
        DisplayName = displayName;
    }

    /// <inheritdoc />
    public Guid UserId { get; }

    /// <inheritdoc />
    public string? Email { get; }

    /// <inheritdoc />
    public string? DisplayName { get; }
}
