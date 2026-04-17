namespace GroundUp.Core.Entities;

/// <summary>
/// Opt-in interface for soft delete behavior.
/// Entities implementing this interface will have delete operations
/// converted to soft deletes by the EF Core interceptor, and a global
/// query filter will exclude soft-deleted records from queries.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>
    /// Whether this entity has been soft-deleted.
    /// </summary>
    bool IsDeleted { get; set; }

    /// <summary>
    /// Timestamp when the entity was soft-deleted.
    /// </summary>
    DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Identifier of the user who soft-deleted the entity.
    /// </summary>
    string? DeletedBy { get; set; }
}
