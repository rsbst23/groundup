using GroundUp.Sample.Data;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Sample.Tests.Integration.Fixtures;

/// <summary>
/// Concrete WebApplicationFactory for the Sample app. Extends the generic
/// GroundUpWebApplicationFactory with Program and SampleDbContext.
/// Demonstrates the pattern consuming applications follow.
/// </summary>
public sealed class SampleApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>
{
}
