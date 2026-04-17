using GroundUp.Core.Entities;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Test entity implementing ISoftDeletable for soft delete unit tests.
/// </summary>
public class SoftDeletableTestEntity : BaseEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
