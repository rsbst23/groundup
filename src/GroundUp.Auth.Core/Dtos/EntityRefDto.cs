namespace GroundUp.Auth.Core.Dtos;

/// <summary>
/// A reference to another entity by id and name, for lists such as "roles using this policy" (GU-96).
/// </summary>
/// <param name="Id">The entity id.</param>
/// <param name="Name">The entity name.</param>
public sealed record EntityRefDto(Guid Id, string Name);