using FsCheck;
using FsCheck.Xunit;
using GroundUp.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace GroundUp.Tests.Unit.Api;

/// <summary>
/// Property-based and unit tests for <see cref="CorrelationIdMiddleware"/>.
/// Validates correlation ID propagation, generation, and storage behavior.
/// </summary>
public sealed class CorrelationIdMiddlewareTests
{
    /// <summary>
    /// Creates a DefaultHttpContext with a response feature that fires OnStarting callbacks
    /// when <see cref="FireOnStartingAsync"/> is called. This is needed because
    /// DefaultHttpContext's OnStarting callbacks don't fire automatically in unit tests.
    /// </summary>
    private static (DefaultHttpContext context, Func<Task> fireOnStarting) CreateContextWithOnStarting()
    {
        var onStartingCallbacks = new List<(Func<object, Task> callback, object state)>();

        var responseFeature = new TestHttpResponseFeature(onStartingCallbacks);
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(responseFeature);
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());

        var context = new DefaultHttpContext(features);

        async Task FireOnStartingAsync()
        {
            // Fire in reverse order (LIFO) — same as ASP.NET Core
            for (var i = onStartingCallbacks.Count - 1; i >= 0; i--)
            {
                await onStartingCallbacks[i].callback(onStartingCallbacks[i].state);
            }
        }

        return (context, FireOnStartingAsync);
    }

    #region Property Tests

    private const string AllowedChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._:-";

    /// <summary>Runs the middleware for one header value; returns the stored and response values.</summary>
    private static (string? Stored, string ResponseHeader) Run(string? headerValue)
    {
        var (context, fireOnStarting) = CreateContextWithOnStarting();
        if (headerValue is not null)
        {
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = headerValue;
        }

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        middleware.InvokeAsync(context).GetAwaiter().GetResult();
        fireOnStarting().GetAwaiter().GetResult();

        return (context.Items["CorrelationId"]?.ToString(),
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    /// <summary>
    /// A valid correlation ID (1 to 128 allowed characters) is stored and returned unchanged.
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(CorrelationIdArbitraries) })]
    public Property InvokeAsync_ValidCorrelationId_PropagatesValueUnchanged(ValidCorrelationId id)
    {
        var (stored, responseHeader) = Run(id.Value);

        return (stored == id.Value && responseHeader == id.Value).ToProperty();
    }

    /// <summary>
    /// An invalid correlation ID (too long, whitespace-only, or containing any character outside the
    /// allowed set) is replaced by a newly generated GUID, in the context item and the response header.
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(CorrelationIdArbitraries) })]
    public Property InvokeAsync_InvalidCorrelationId_ReplacedWithNewGuid(InvalidCorrelationId id)
    {
        var (stored, responseHeader) = Run(id.Value);

        return (stored != id.Value
                && Guid.TryParse(stored, out _)
                && responseHeader == stored).ToProperty();
    }

    /// <summary>Wraps a generated valid correlation ID.</summary>
    public sealed record ValidCorrelationId(string Value)
    {
        public override string ToString() => Value;
    }

    /// <summary>Wraps a generated invalid correlation ID.</summary>
    public sealed record InvalidCorrelationId(string Value)
    {
        public override string ToString() => string.Concat(Value.Select(c => c < 32 || c > 126 ? $"\\u{(int)c:x4}" : c.ToString()));
    }

    /// <summary>Generators that produce each class explicitly, so coverage does not depend on chance.</summary>
    public static class CorrelationIdArbitraries
    {
        private static Gen<string> AllowedString(int minLength, int maxLength) =>
            from length in Gen.Choose(minLength, maxLength)
            from chars in Gen.ArrayOf(length, Gen.Elements(AllowedChars.ToCharArray()))
            select new string(chars);

        public static Arbitrary<ValidCorrelationId> Valid() =>
            Arb.From(AllowedString(1, CorrelationIdMiddleware.MaxLength).Select(s => new ValidCorrelationId(s)));

        public static Arbitrary<InvalidCorrelationId> Invalid()
        {
            // Allowed text with one disallowed character inserted at a random position
            var disallowed = Gen.Elements('\t', '\r', '\n', '\0', ' ', '/', '<', '"', '=', 'é', '\u2028', '\u007f');
            var withBadChar =
                from text in AllowedString(0, 60)
                from bad in disallowed
                from position in Gen.Choose(0, text.Length)
                select text.Insert(position, bad.ToString());

            var tooLong = AllowedString(CorrelationIdMiddleware.MaxLength + 1, 300);

            var whitespaceOnly =
                from length in Gen.Choose(1, 10)
                from chars in Gen.ArrayOf(length, Gen.Elements(' ', '\t'))
                select new string(chars);

            return Arb.From(Gen.OneOf(withBadChar, tooLong, whitespaceOnly).Select(s => new InvalidCorrelationId(s)));
        }
    }

    #endregion

    #region Unit Tests

    [Fact]
    public async Task InvokeAsync_RequestHasCorrelationIdHeader_UsesProvidedValue()
    {
        // Arrange
        var expectedId = "test-correlation-id-123";
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = expectedId;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(expectedId, context.Items["CorrelationId"]?.ToString());
    }

    [Fact]
    public async Task InvokeAsync_RequestMissingCorrelationIdHeader_GeneratesNewGuid()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var correlationId = context.Items["CorrelationId"]?.ToString();
        Assert.NotNull(correlationId);
        Assert.True(Guid.TryParse(correlationId, out _), $"Expected a valid GUID but got: {correlationId}");
    }

    [Fact]
    public async Task InvokeAsync_StoresCorrelationIdInHttpContextItems()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var correlationId = context.Items["CorrelationId"]?.ToString();
        Assert.NotNull(correlationId);
        Assert.NotEmpty(correlationId);
    }

    [Fact]
    public async Task InvokeAsync_AddsCorrelationIdToResponseHeaders()
    {
        // Arrange
        var expectedId = "response-header-test-id";
        var (context, fireOnStarting) = CreateContextWithOnStarting();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = expectedId;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);
        await fireOnStarting();

        // Assert
        var responseHeader = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.Equal(expectedId, responseHeader);
    }

    [Fact]
    public void InvokeAsync_ExactlyMaxLength_IsAccepted()
    {
        var value = new string('a', CorrelationIdMiddleware.MaxLength);

        var (stored, _) = Run(value);

        Assert.Equal(value, stored);
    }

    [Fact]
    public void InvokeAsync_OneOverMaxLength_IsReplaced()
    {
        var value = new string('a', CorrelationIdMiddleware.MaxLength + 1);

        var (stored, _) = Run(value);

        Assert.NotEqual(value, stored);
        Assert.True(Guid.TryParse(stored, out _));
    }

    [Theory]
    [InlineData("a\tb")]
    [InlineData("a\r\nb")]
    [InlineData("id\r\nX-Injected: 1")]
    [InlineData("")]
    public void InvokeAsync_ControlCharactersOrEmpty_AreReplaced(string value)
    {
        var (stored, responseHeader) = Run(value);

        Assert.NotEqual(value, stored);
        Assert.True(Guid.TryParse(stored, out _));
        Assert.Equal(stored, responseHeader);
    }

    [Theory]
    [InlineData("0b8f6e3a-1c2d-4e5f-8a9b-0c1d2e3f4a5b")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")]
    [InlineData("00f067aa0ba902b7")]
    [InlineData("req_2024.11:abc-DEF")]
    public void IsValid_CommonIdFormats_AreAccepted(string value)
    {
        Assert.True(CorrelationIdMiddleware.IsValid(value));
    }

    [Fact]
    public async Task InvokeAsync_CallsNextDelegate()
    {
        // Arrange
        var nextCalled = false;
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    #endregion

    #region Test Infrastructure

    /// <summary>
    /// Custom IHttpResponseFeature that captures OnStarting callbacks
    /// so they can be fired manually in unit tests.
    /// </summary>
    private sealed class TestHttpResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> callback, object state)> _onStartingCallbacks;

        public TestHttpResponseFeature(List<(Func<object, Task> callback, object state)> onStartingCallbacks)
        {
            _onStartingCallbacks = onStartingCallbacks;
        }

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => false;

        public void OnStarting(Func<object, Task> callback, object state)
        {
            _onStartingCallbacks.Add((callback, state));
        }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    #endregion
}
