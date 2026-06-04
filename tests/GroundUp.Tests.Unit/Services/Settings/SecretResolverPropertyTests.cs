using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Entities.Settings;
using GroundUp.Core.Enums;
using GroundUp.Core.Models;
using GroundUp.Services.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using GroundUp.Events;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Property-based tests for secret reference resolution in <see cref="SettingsService"/>.
/// Validates single-pass resolution semantics and write-path literal persistence.
///
/// **Validates: Requirements 4.6, 4.8**
/// </summary>
public sealed class SecretResolverPropertyTests : IDisposable
{
    private readonly SettingsTestFixture _fixture;

    public SecretResolverPropertyTests()
    {
        _fixture = new SettingsTestFixture();
    }

    /// <summary>
    /// Property 6: Single-Pass Secret Reference Resolution.
    /// For any non-whitespace secret reference value:
    ///   - The resolver is called exactly once on read
    ///   - The resolved value is returned verbatim even if it starts with "secretref://"
    ///   - Write paths persist the literal "secretref://..." without calling the resolver
    ///
    /// **Validates: Requirements 4.6, 4.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ResolverCalledExactlyOnce_ResultReturnedVerbatim(NonEmptyString secretPath)
    {
        var path = secretPath.Get;

        Func<bool> property = () =>
        {
            var secretRefValue = $"secretref://{path}";
            var resolvedValue = $"resolved-secret-for-{path}";

            using var context = _fixture.CreateContext();

            var secretResolver = Substitute.For<ISecretResolver>();
            secretResolver.ResolveAsync(secretRefValue, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<string?>(resolvedValue));

            var service = CreateServiceWithResolver(context, secretResolver);

            // Seed a definition and value with the secretref:// literal
            var levelId = SettingsTestFixture.SeedLevelAsync(context).GetAwaiter().GetResult();
            var definition = SettingsTestFixture.SeedDefinitionAsync(
                context,
                key: $"test.secret.{Guid.NewGuid():N}",
                dataType: SettingDataType.String,
                allowedLevelIds: levelId).GetAwaiter().GetResult();

            SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, secretRefValue)
                .GetAwaiter().GetResult();

            var scopeChain = new List<SettingScopeEntry>
            {
                new(levelId, null)
            };

            // Act: read the value
            var result = service.GetAsync<string>(definition.Key, scopeChain).GetAwaiter().GetResult();

            // Assert: resolver called exactly once
            secretResolver.Received(1).ResolveAsync(secretRefValue, Arg.Any<CancellationToken>());

            // Assert: result is the resolved value
            return result.Success && result.Data == resolvedValue;
        };

        return property.When(!string.IsNullOrWhiteSpace(path));
    }

    /// <summary>
    /// Property 6 (single-pass): When the resolver returns a value that itself starts
    /// with "secretref://", that value is returned verbatim without re-resolution.
    ///
    /// **Validates: Requirements 4.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ResolverReturnsSecretRef_NotReResolved(NonEmptyString innerPath)
    {
        var path = innerPath.Get;

        Func<bool> property = () =>
        {
            var secretRefValue = $"secretref://outer/{path}";
            // The resolver returns a value that itself starts with secretref://
            var resolvedValue = $"secretref://inner/{path}";

            using var context = _fixture.CreateContext();

            var secretResolver = Substitute.For<ISecretResolver>();
            secretResolver.ResolveAsync(secretRefValue, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<string?>(resolvedValue));

            var service = CreateServiceWithResolver(context, secretResolver);

            // Seed a definition and value
            var levelId = SettingsTestFixture.SeedLevelAsync(context).GetAwaiter().GetResult();
            var definition = SettingsTestFixture.SeedDefinitionAsync(
                context,
                key: $"test.nested.{Guid.NewGuid():N}",
                dataType: SettingDataType.String,
                allowedLevelIds: levelId).GetAwaiter().GetResult();

            SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, secretRefValue)
                .GetAwaiter().GetResult();

            var scopeChain = new List<SettingScopeEntry>
            {
                new(levelId, null)
            };

            // Act: read the value
            var result = service.GetAsync<string>(definition.Key, scopeChain).GetAwaiter().GetResult();

            // Assert: resolver called exactly once (for the outer ref only, not re-resolved)
            secretResolver.Received(1).ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

            // Assert: the returned value is the inner secretref:// literal (single-pass)
            return result.Success && result.Data == resolvedValue;
        };

        return property.When(!string.IsNullOrWhiteSpace(path));
    }

    /// <summary>
    /// Property 6 (write path): SetAsync persists the "secretref://" literal as-is
    /// without calling the resolver. The resolver receives zero calls on write.
    ///
    /// **Validates: Requirements 4.6**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property WritePathPersistsLiteral_ResolverNotCalled(NonEmptyString secretPath)
    {
        var path = secretPath.Get;

        Func<bool> property = () =>
        {
            var secretRefValue = $"secretref://{path}";

            using var context = _fixture.CreateContext();

            var secretResolver = Substitute.For<ISecretResolver>();
            var service = CreateServiceWithResolver(context, secretResolver);

            // Seed a definition
            var levelId = SettingsTestFixture.SeedLevelAsync(context).GetAwaiter().GetResult();
            var definition = SettingsTestFixture.SeedDefinitionAsync(
                context,
                key: $"test.write.{Guid.NewGuid():N}",
                dataType: SettingDataType.String,
                allowedLevelIds: levelId).GetAwaiter().GetResult();

            // Act: write the secretref:// value
            var result = service.SetAsync(definition.Key, secretRefValue, levelId, null)
                .GetAwaiter().GetResult();

            // Assert: resolver was never called on write path
            secretResolver.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

            // Assert: the value was persisted as the literal secretref:// string
            var storedValue = context.Set<SettingValue>()
                .FirstOrDefault(v => v.SettingDefinitionId == definition.Id);

            return result.Success && storedValue?.Value == secretRefValue;
        };

        return property.When(!string.IsNullOrWhiteSpace(path));
    }

    private SettingsService CreateServiceWithResolver(
        TestSettingsDbContext context,
        ISecretResolver? secretResolver)
    {
        return new SettingsService(
            context,
            _fixture.EventBus,
            _fixture.ScopeChainProvider,
            _fixture.MemoryCache,
            _fixture.CacheOptions,
            _fixture.CacheKeyTracker,
            encryptionProvider: null,
            secretResolver: secretResolver);
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }
}
