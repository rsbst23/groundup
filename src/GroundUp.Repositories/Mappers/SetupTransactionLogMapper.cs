using GroundUp.Core.Dtos.Setup;
using GroundUp.Core.Entities;
using Riok.Mapperly.Abstractions;

namespace GroundUp.Repositories.Mappers;

/// <summary>
/// Mapperly source-generated mapper for <see cref="SetupTransactionLog"/> conversions.
/// </summary>
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class SetupTransactionLogMapper
{
    /// <summary>Maps a SetupTransactionLog entity to a SetupTransactionLogDto.</summary>
    public static partial SetupTransactionLogDto ToDto(SetupTransactionLog entity);

    /// <summary>Projects a SetupTransactionLog query to SetupTransactionLogDto (translated to SQL).</summary>
    public static partial IQueryable<SetupTransactionLogDto> ProjectToDto(this IQueryable<SetupTransactionLog> query);

    /// <summary>Maps a new-row DTO to an entity. Id and audit fields are set by the database layer.</summary>
    [MapperIgnoreTarget(nameof(SetupTransactionLog.Id))]
    [MapperIgnoreTarget(nameof(SetupTransactionLog.ExternalUserId))]
    [MapperIgnoreTarget(nameof(SetupTransactionLog.ErrorMessage))]
    [MapperIgnoreTarget(nameof(SetupTransactionLog.CreatedAt))]
    [MapperIgnoreTarget(nameof(SetupTransactionLog.CreatedBy))]
    [MapperIgnoreTarget(nameof(SetupTransactionLog.UpdatedAt))]
    [MapperIgnoreTarget(nameof(SetupTransactionLog.UpdatedBy))]
    public static partial SetupTransactionLog ToEntity(CreateSetupTransactionLogDto dto);
}
