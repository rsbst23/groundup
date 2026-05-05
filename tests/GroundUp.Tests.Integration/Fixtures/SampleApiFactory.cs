using GroundUp.Sample.Data;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Tests.Integration.Fixtures;

/// <summary>
/// WebApplicationFactory for general sample app integration tests.
/// Uses the Sample app as the entry point with Testcontainers Postgres.
/// </summary>
public sealed class SampleApiFactory : GroundUpWebApplicationFactory<Program, SampleDbContext>
{
}

/// <summary>
/// xUnit collection definition that shares a single <see cref="SampleApiFactory"/>
/// across all sample API integration test classes.
/// </summary>
[CollectionDefinition("SampleApi")]
public sealed class SampleApiCollection : ICollectionFixture<SampleApiFactory>
{
}
