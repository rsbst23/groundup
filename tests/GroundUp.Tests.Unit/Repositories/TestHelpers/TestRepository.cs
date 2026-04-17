using GroundUp.Repositories;

namespace GroundUp.Tests.Unit.Repositories.TestHelpers;

/// <summary>
/// Concrete repository for unit testing BaseRepository with TestEntity.
/// Uses simple property-copy mapping (not Mapperly, since this is a test helper).
/// </summary>
public class TestRepository : BaseRepository<TestEntity, TestDto>
{
    public TestRepository(TestDbContext context)
        : base(context, MapEntityToDto, MapDtoToEntity)
    {
    }

    private static TestDto MapEntityToDto(TestEntity entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Score = entity.Score,
        CreatedDate = entity.CreatedDate,
        CategoryId = entity.CategoryId,
        Description = entity.Description
    };

    private static TestEntity MapDtoToEntity(TestDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Score = dto.Score,
        CreatedDate = dto.CreatedDate,
        CategoryId = dto.CategoryId,
        Description = dto.Description
    };
}
