using GroundUp.Core.Entities;

namespace GroundUp.Tests.Unit.Data.Postgres.TestHelpers;

/// <summary>
/// Test entity that extends BaseEntity and implements IAuditable.
/// Used to verify audit field population by the AuditableInterceptor.
/// </summary>
public sealed class AuditableTestEntity : BaseEntity, IAuditable
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
