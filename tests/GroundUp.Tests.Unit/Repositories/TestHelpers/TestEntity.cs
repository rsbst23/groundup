using GroundUp.Core.Entities;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Simple test entity for BaseRepository unit tests.
/// </summary>
public class TestEntity : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Description { get; set; }
}
