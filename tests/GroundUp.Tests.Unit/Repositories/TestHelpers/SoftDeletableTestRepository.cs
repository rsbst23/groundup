using GroundUp.Repositories;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Concrete repository for unit testing BaseRepository soft delete behavior.
/// </summary>
public class SoftDeletableTestRepository : BaseRepository<SoftDeletableTestEntity, TestDto>
{
    public SoftDeletableTestRepository(TestDbContext context)
        : base(context, MapEntityToDto, MapDtoToEntity)
    {
    }

    private static TestDto MapEntityToDto(SoftDeletableTestEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name
    };

    private static SoftDeletableTestEntity MapDtoToEntity(TestDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name
    };
}
