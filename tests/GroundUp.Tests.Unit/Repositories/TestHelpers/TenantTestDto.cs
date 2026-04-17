namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Simple DTO for BaseTenantRepository unit tests.
/// </summary>
public class TenantTestDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}
