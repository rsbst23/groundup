using System.Text;
using System.Text.Json;
using GroundUp.Core.Results;

namespace GroundUp.Tests.Common.Fixtures;

/// <summary>
/// Abstract base class for HTTP integration tests. Provides an <see cref="HttpClient"/>
/// and JSON serialization helpers for working with <see cref="OperationResult{T}"/> responses.
/// <para>
/// Test classes inherit from this and pass an <see cref="HttpClient"/> created from their
/// <see cref="GroundUpWebApplicationFactory{TEntryPoint, TContext}"/> via <c>factory.CreateClient()</c>.
/// </para>
/// </summary>
public abstract class IntegrationTestBase
{
    /// <summary>
    /// Shared JSON serializer options configured with camelCase naming and case-insensitive
    /// property matching to align with ASP.NET Core's default JSON serialization behavior.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The <see cref="HttpClient"/> configured to send requests to the test host.
    /// </summary>
    protected HttpClient Client { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="IntegrationTestBase"/>.
    /// </summary>
    /// <param name="client">
    /// An <see cref="HttpClient"/> created from the test factory via <c>factory.CreateClient()</c>.
    /// </param>
    protected IntegrationTestBase(HttpClient client)
    {
        Client = client;
    }

    /// <summary>
    /// Serializes the specified object to a <see cref="StringContent"/> with
    /// <c>application/json</c> media type using camelCase JSON naming.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="obj">The object to serialize.</param>
    /// <returns>A <see cref="StringContent"/> containing the JSON representation.</returns>
    protected static StringContent ToJsonContent<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// Reads the response body and deserializes it to an <see cref="OperationResult{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the <c>Data</c> payload in the result.</typeparam>
    /// <param name="response">The HTTP response message to read.</param>
    /// <returns>The deserialized <see cref="OperationResult{T}"/>, or <c>null</c> if deserialization fails.</returns>
    protected static async Task<OperationResult<T>?> ReadResultAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OperationResult<T>>(content, JsonOptions);
    }

    /// <summary>
    /// Reads the response body and deserializes it to a non-generic <see cref="OperationResult"/>.
    /// </summary>
    /// <param name="response">The HTTP response message to read.</param>
    /// <returns>The deserialized <see cref="OperationResult"/>, or <c>null</c> if deserialization fails.</returns>
    protected static async Task<OperationResult?> ReadResultAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<OperationResult>(content, JsonOptions);
    }
}
