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

    /// <summary>
    /// Property 3: Correlation ID header propagation.
    /// For any non-empty string value provided as the X-Correlation-Id request header,
    /// the middleware stores that exact value in HttpContext.Items["CorrelationId"]
    /// and adds that exact value to the response X-Correlation-Id header.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InvokeAsync_AnyNonEmptyCorrelationId_PropagatesValueUnchanged(NonEmptyString correlationId)
    {
        var value = correlationId.Get;
        var (context, fireOnStarting) = CreateContextWithOnStarting();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = value;

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        middleware.InvokeAsync(context).GetAwaiter().GetResult();
        fireOnStarting().GetAwaiter().GetResult();

        var storedValue = context.Items["CorrelationId"]?.ToString();
        var responseHeader = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();

        return (storedValue == value && responseHeader == value).ToProperty();
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
