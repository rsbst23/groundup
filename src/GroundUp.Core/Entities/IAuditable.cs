namespace GroundUp.Core.Entities;

/// <summary>
/// Opt-in interface for automatic audit field population.
/// Entities implementing this interface will have their audit fields
/// set automatically by the EF Core SaveChanges interceptor.
/// </summary>
public interface IAuditable
{
    /// <summary>
    /// Timestamp when the entity was created. Set automatically on insert.
    /// </summary>
    DateTime CreatedAt { get; set; }

    /// <summary>
    /// Identifier of the user who created the entity. Set automatically from ICurrentUser.
    /// </summary>
    string? CreatedBy { get; set; }

    /// <summary>
    /// Timestamp when the entity was last updated. Set automatically on update.
    /// </summary>
    DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Identifier of the user who last updated the entity. Set automatically from ICurrentUser.
    /// </summary>
    string? UpdatedBy { get; set; }
}
