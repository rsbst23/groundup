using GroundUp.Core.Abstractions;
using GroundUp.Repositories;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Concrete repository for unit testing BaseTenantRepository with TenantTestEntity.
/// Uses simple property-copy mapping (not Mapperly, since this is a test helper).
/// </summary>
public class TenantTestRepository : BaseTenantRepository<TenantTestEntity, TenantTestDto>
{
    public TenantTestRepository(TestDbContext context, ITenantContext tenantContext)
        : base(context, tenantContext, MapEntityToDto, MapDtoToEntity)
    {
    }

    private static TenantTestDto MapEntityToDto(TenantTestEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        TenantId = entity.TenantId
    };

    private static TenantTestEntity MapDtoToEntity(TenantTestDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        TenantId = dto.TenantId
    };
}
