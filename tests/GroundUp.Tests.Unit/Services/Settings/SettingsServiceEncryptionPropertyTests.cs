using FsCheck;
using FsCheck.Xunit;
using GroundUp.Core.Abstractions;
using GroundUp.Core.Models;
using GroundUp.Services.Settings;
using GroundUp.Tests.Unit.Services.Settings.TestHelpers;
using NSubstitute;

namespace GroundUp.Tests.Unit.Services.Settings;

/// <summary>
/// Property-based tests for <see cref="SettingsService"/> encryption short-circuit behavior.
/// Validates that null/empty/whitespace values are persisted as null without calling the
/// encryption provider, and that non-whitespace values call Encrypt exactly once.
/// </summary>
public sealed class SettingsServiceEncryptionPropertyTests : IDisposable
{
    private readonly SettingsTestFixture _fixture = new();

    /// <summary>
    /// Property 5: SettingsService Short-Circuits Whitespace Before Calling Provider.
    /// For any string that is null, empty, or whitespace-only:
    ///   SetAsync persists null AND Encrypt is never called.
    /// For any non-whitespace string:
    ///   SetAsync calls Encrypt exactly once.
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SetAsync_WhitespaceInput_PersistsNullWithoutCallingEncrypt(int seed)
    {
        // Generate whitespace-only strings: spaces, tabs, newlines, empty, or combinations
        var whitespaceChars = new[] { ' ', '\t', '\n', '\r', '\v', '\f' };
        var rng = new System.Random(seed);
        var length = rng.Next(0, 10); // 0 = empty string
        var whitespace = new string(Enumerable.Range(0, length)
            .Select(_ => whitespaceChars[rng.Next(whitespaceChars.Length)])
            .ToArray());

        // Use a fresh context per iteration to avoid UNIQUE constraint collisions
        using var context = _fixture.CreateContext();
        var encryptionProvider = Substitute.For<ISettingEncryptionProvider>();
        var service = _fixture.CreateService(context, encryptionProvider);

        // Use Guid to ensure unique keys across all iterations
        var uniqueKey = $"EncSetting_{Guid.NewGuid():N}";
        var levelId = SettingsTestFixture.SeedLevelAsync(context, $"Level_{Guid.NewGuid():N}")
            .GetAwaiter().GetResult();
        SettingsTestFixture.SeedDefinitionAsync(context,
            key: uniqueKey,
            isEncrypted: true,
            allowedLevelIds: levelId).GetAwaiter().GetResult();

        // Act
        var result = service.SetAsync(uniqueKey, whitespace, levelId, null).GetAwaiter().GetResult();

        // Assert: succeeds, persists null, Encrypt never called
        var succeeds = result.Success;
        var persistedNull = result.Data?.Value is null;
        encryptionProvider.DidNotReceive().Encrypt(Arg.Any<string>());
        var encryptNotCalled = true; // If DidNotReceive didn't throw, it wasn't called

        return (succeeds && persistedNull && encryptNotCalled).ToProperty();
    }

    /// <summary>
    /// Property 5 (non-whitespace case): For any non-whitespace string,
    /// SetAsync on an IsEncrypted=true definition calls Encrypt exactly once.
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SetAsync_NonWhitespaceInput_CallsEncryptExactlyOnce(NonEmptyString input)
    {
        var value = input.Get;

        Func<bool> property = () =>
        {
            using var context = _fixture.CreateContext();
            var encryptionProvider = Substitute.For<ISettingEncryptionProvider>();
            encryptionProvider.Encrypt(Arg.Any<string>()).Returns(ci => $"encrypted:{ci.Arg<string>()}");

            var service = _fixture.CreateService(context, encryptionProvider);

            // Seed a level and an encrypted definition with a unique key
            var uniqueKey = $"EncSetting_{Guid.NewGuid():N}";
            var levelId = SettingsTestFixture.SeedLevelAsync(context, $"Level_{Guid.NewGuid():N}")
                .GetAwaiter().GetResult();
            SettingsTestFixture.SeedDefinitionAsync(context,
                key: uniqueKey,
                isEncrypted: true,
                allowedLevelIds: levelId).GetAwaiter().GetResult();

            // Act
            var result = service.SetAsync(uniqueKey, value, levelId, null).GetAwaiter().GetResult();

            // Assert: succeeds and Encrypt was called exactly once with the input value
            if (!result.Success) return false;

            encryptionProvider.Received(1).Encrypt(value);
            return result.Data?.Value == $"encrypted:{value}";
        };

        return property.When(!string.IsNullOrWhiteSpace(value));
    }

    /// <summary>
    /// Property 5 (read path): For any stored whitespace/empty value on an
    /// IsEncrypted=true definition, GetAsync does NOT call Decrypt.
    /// The key property: Decrypt is never invoked for empty/whitespace stored values.
    /// **Validates: Requirements 3.3, 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetAsync_WhitespaceStoredValue_DoesNotCallDecrypt(int seed)
    {
        // Generate whitespace-only strings (non-null) to store in the database
        var whitespaceChars = new[] { ' ', '\t', '\n', '\r', '\v', '\f' };
        var rng = new System.Random(seed);
        var length = rng.Next(0, 10);
        var whitespace = length == 0
            ? string.Empty
            : new string(Enumerable.Range(0, length)
                .Select(_ => whitespaceChars[rng.Next(whitespaceChars.Length)])
                .ToArray());

        using var context = _fixture.CreateContext();
        var encryptionProvider = Substitute.For<ISettingEncryptionProvider>();
        var service = _fixture.CreateService(context, encryptionProvider);

        var uniqueKey = $"EncRead_{Guid.NewGuid():N}";
        var levelId = SettingsTestFixture.SeedLevelAsync(context, $"ReadLevel_{Guid.NewGuid():N}")
            .GetAwaiter().GetResult();
        // Use null default to isolate the test to stored-value behavior only
        var definition = SettingsTestFixture.SeedDefinitionAsync(context,
            key: uniqueKey,
            isEncrypted: true,
            defaultValue: null,
            allowedLevelIds: levelId).GetAwaiter().GetResult();

        // Seed a stored value that is whitespace/empty
        SettingsTestFixture.SeedValueAsync(context, definition.Id, levelId, null, whitespace)
            .GetAwaiter().GetResult();

        // Act
        var scopeChain = new[] { new SettingScopeEntry(levelId, null) };
        var result = service.GetAsync<string>(uniqueKey, scopeChain).GetAwaiter().GetResult();

        // Assert: Decrypt never called — this is the core property being validated
        encryptionProvider.DidNotReceive().Decrypt(Arg.Any<string>());

        // The result should be successful
        return result.Success.ToProperty();
    }

    public void Dispose() => _fixture.Dispose();
}
