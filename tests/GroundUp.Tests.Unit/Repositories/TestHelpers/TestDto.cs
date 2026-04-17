namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Simple DTO for BaseRepository unit tests.
/// </summary>
public class TestDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Score { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Description { get; set; }
}
