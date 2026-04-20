using GroundUp.Core.Entities;

namespace GroundUp.Tests.Unit.Data.Postgres.TestHelpers;

/// <summary>
/// Test entity that extends BaseEntity only — does NOT implement IAuditable or ISoftDeletable.
/// Used to verify that interceptors skip entities without audit/soft-delete interfaces.
/// </summary>
public sealed class NonAuditableTestEntity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
}
