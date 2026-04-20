using GroundUp.Core.Entities;

namespace GroundUp.Tests.Unit.Data.Postgres.TestHelpers;

/// <summary>
/// Test entity that extends BaseEntity and implements both IAuditable and ISoftDeletable.
/// Used to verify soft delete interception and audit field population together.
/// </summary>
public sealed class SoftDeletableAuditableTestEntity : BaseEntity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
