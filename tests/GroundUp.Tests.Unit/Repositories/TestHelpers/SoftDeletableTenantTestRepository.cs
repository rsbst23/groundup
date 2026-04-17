using GroundUp.Core.Abstractions;
using GroundUp.Repositories;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Concrete repository for unit testing BaseTenantRepository soft delete behavior.
/// </summary>
public class SoftDeletableTenantTestRepository : BaseTenantRepository<SoftDeletableTenantTestEntity, TenantTestDto>
{
    public SoftDeletableTenantTestRepository(TestDbContext context, ITenantContext tenantContext)
        : base(context, tenantContext, MapEntityToDto, MapDtoToEntity)
    {
    }

    private static TenantTestDto MapEntityToDto(SoftDeletableTenantTestEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        TenantId = entity.TenantId
    };

    private static SoftDeletableTenantTestEntity MapDtoToEntity(TenantTestDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        TenantId = dto.TenantId
    };
}
