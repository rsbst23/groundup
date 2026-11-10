namespace GroundUp.Tests.Integration.Auth;

/// <summary>
/// xUnit collection definition that shares a single <see cref="AuthFlowTestFixture"/>
/// across all test classes in the "AuthFlow" collection.
/// <para>
/// Tests in this collection exercise end-to-end auth flows through the full middleware
/// pipeline (WebApplicationFactory against the Sample app) with a real Postgres database
/// and mocked Keycloak responses.
/// </para>
/// </summary>
[CollectionDefinition("AuthFlow")]
public sealed class AuthFlowCollection : ICollectionFixture<AuthFlowTestFixture>
{
}

/// <summary>
/// xUnit collection for auth flow tests that need a configured default tenant. Uses
/// <see cref="DefaultTenantAuthFlowFixture"/>, which has its own database, so the default-tenant
/// setting never reaches tests in the "AuthFlow" collection that expect no default tenant.
/// </summary>
[CollectionDefinition("AuthFlowDefaultTenant")]
public sealed class AuthFlowDefaultTenantCollection : ICollectionFixture<DefaultTenantAuthFlowFixture>
{
}
