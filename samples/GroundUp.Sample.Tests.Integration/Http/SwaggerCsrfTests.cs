using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GroundUp.Sample.Swagger;
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
    private const string Marker = "JSON.parse('";
    private const string InterceptorsMarker = "var interceptors = JSON.parse('";

    public SwaggerCsrfTests(SampleApiFactory factory) : base(factory.CreateClient())
    {
    }

    [Fact]
    public void Interceptors_AreSingleLineWithoutBackslashes()
    {
        // Swashbuckle embeds the functions inside a JavaScript string literal; a backslash there
        // breaks the page (Swagger UI renders blank).
        SwaggerCsrfInterceptors.Request.Should().NotContain("\\").And.NotContain("\n").And.NotContain("\r");
        SwaggerCsrfInterceptors.Response.Should().NotContain("\\").And.NotContain("\n").And.NotContain("\r");
    }

    [Fact]
    public async Task SwaggerUi_InterceptorConfigurationParsesInTheBrowser()
    {
        // Arrange — Swashbuckle 6 serves the configuration in index.html or index.js
        var page = await Client.GetStringAsync("/swagger/index.html");
        var jsResponse = await Client.GetAsync("/swagger/index.js");
        if (jsResponse.StatusCode == HttpStatusCode.OK)
        {
            page += await jsResponse.Content.ReadAsStringAsync();
        }

        // Act + Assert — do what the browser does for every embedded configuration: evaluate the
        // JS string literal, then JSON.parse it. One invalid escape leaves Swagger UI blank.
        var count = 0;
        for (var at = page.IndexOf(Marker, StringComparison.Ordinal); at >= 0; at = page.IndexOf(Marker, at + 1, StringComparison.Ordinal))
        {
            var embedded = ReadSingleQuotedLiteral(page, at + Marker.Length);
            var parseEmbedded = () => JsonDocument.Parse(embedded).Dispose();
            parseEmbedded.Should().NotThrow("an invalid escape here leaves Swagger UI blank");
            count++;
        }

        count.Should().BeGreaterThan(0, "the page should embed its configuration");

        var start = page.IndexOf(InterceptorsMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the page should embed the interceptor configuration");
        using var json = JsonDocument.Parse(ReadSingleQuotedLiteral(page, start + InterceptorsMarker.Length));
        var request = json.RootElement.GetProperty("RequestInterceptorFunction").GetString();
        var response = json.RootElement.GetProperty("ResponseInterceptorFunction").GetString();
        request.Should().Contain("X-CSRF-Token").And.Contain("/auth/csrf");
        response.Should().Contain("groundupCsrfToken = null");
    }

    /// <summary>
    /// Returns the value of a JavaScript single-quoted string literal starting at
    /// <paramref name="index"/>, applying JavaScript escape rules (an unknown escape such as
    /// <c>\/</c> or <c>\?</c> drops the backslash, as browsers do).
    /// </summary>
    private static string ReadSingleQuotedLiteral(string source, int index)
    {
        var sb = new StringBuilder();
        for (var i = index; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '\'')
            {
                return sb.ToString();
            }

            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            var next = source[++i];
            switch (next)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    sb.Append((char)int.Parse(source.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(next); break;
            }
        }

        throw new InvalidOperationException("Unterminated string literal.");
    }
}
