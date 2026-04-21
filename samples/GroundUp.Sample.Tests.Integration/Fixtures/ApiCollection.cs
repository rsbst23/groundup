using Xunit;

namespace GroundUp.Sample.Tests.Integration.Fixtures;

/// <summary>
/// xUnit collection definition that shares a single SampleApiFactory
/// across all test classes in the "Api" collection.
/// </summary>
[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<SampleApiFactory>
{
}
