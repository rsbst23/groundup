using System.Net;
using FluentAssertions;
using GroundUp.Sample.Tests.Integration.Fixtures;
using GroundUp.Tests.Common.Fixtures;

namespace GroundUp.Sample.Tests.Integration.Http;

/// <summary>
/// The sample's Swagger UI is configured with the CSRF interceptors, so "Try it out" requests
/// that use the auth cookie can pass CSRF protection.
/// </summary>
[Collection("Api")]
public sealed class SwaggerCsrfTests : IntegrationTestBase
{
    public SwaggerCsrfTests(SampleApiFactory factory) : base(factory.CreateClient())
    {
    }

    [Fact]
    public async Task SwaggerUi_IncludesCsrfRequestAndResponseInterceptors()
    {
        // Act — Swashbuckle 6 places the UI configuration in index.html or index.js depending on version
        var html = await Client.GetStringAsync("/swagger/index.html");
        var jsResponse = await Client.GetAsync("/swagger/index.js");
        var js = jsResponse.StatusCode == HttpStatusCode.OK ? await jsResponse.Content.ReadAsStringAsync() : string.Empty;
        var page = html + js;

        // Assert
        page.Should().Contain("X-CSRF-Token");
        page.Should().Contain("/auth/csrf");
        page.Should().Contain("groundupCsrfToken = null");
    }
}
