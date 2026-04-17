namespace GroundUp.Core.Entities;

/// <summary>
/// Abstract base entity providing a UUID v7 identity for all framework entities.
/// All entities in the GroundUp framework should inherit from this class.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Unique identifier. Generated as UUID v7 (sequential, sortable) by default.
    /// </summary>
    public Guid Id { get; set; }
}
