using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Validators;

namespace GroundUp.Tests.Unit.Authentication;

/// <summary>
/// Property-based tests for permission key validation.
/// Feature: phase-9a-auth-entities, Property 4: Permission key validation
/// Validates: Requirements 18.6
/// </summary>
public sealed class PermissionKeyValidatorPropertyTests
{
    private static readonly CreatePermissionDtoValidator Validator = new();

    /// <summary>
    /// Property 4: Valid permission keys produce zero validation errors on the Key field.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidPermissionKeyArbitrary) })]
    public Property CreatePermissionDto_ValidKey_ProducesNoErrors(ValidPermissionKey key)
    {
        var dto = new CreatePermissionDto(
            Key: key.Value,
            Name: "Valid Permission",
            Description: null,
            Module: "testmodule");

        var result = Validator.Validate(dto);

        return result.IsValid.ToProperty()
            .Label($"Key '{key.Value}' should be valid");
    }

    /// <summary>
    /// Property 4: Invalid permission keys produce at least one error on the Key field.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InvalidPermissionKeyArbitrary) })]
    public Property CreatePermissionDto_InvalidKey_ProducesKeyError(InvalidPermissionKey key)
    {
        var dto = new CreatePermissionDto(
            Key: key.Value,
            Name: "Valid Permission",
            Description: null,
            Module: "testmodule");

        var result = Validator.Validate(dto);
        var keyErrors = result.Errors.Where(e => e.PropertyName == "Key").ToList();

        return (keyErrors.Count > 0).ToProperty()
            .Label($"Key '{key.Value}' should be invalid");
    }

    // --- Custom types and generators ---

    public record ValidPermissionKey(string Value);
    public record InvalidPermissionKey(string Value);

    public static class ValidPermissionKeyArbitrary
    {
        private static readonly char[] SegmentStartChars = "abcdefghijklmnopqrstuvwxyz".ToCharArray();
        private static readonly char[] SegmentChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        public static Arbitrary<ValidPermissionKey> ValidPermissionKeyArb()
        {
            // Generate a valid segment: starts with [a-z], followed by [a-z0-9]*
            var segmentGen = Gen.Choose(1, 15)
                .SelectMany(len =>
                {
                    var firstChar = Gen.Elements(SegmentStartChars).Select(c => c.ToString());
                    if (len == 1)
                        return firstChar;

                    var rest = Gen.ArrayOf(len - 1, Gen.Elements(SegmentChars))
                        .Select(chars => new string(chars));

                    return firstChar.SelectMany(f => rest.Select(r => f + r));
                });

            // Generate 1–5 segments joined by dots
            var keyGen = Gen.Choose(1, 5)
                .SelectMany(segCount =>
                    Gen.ArrayOf(segCount, segmentGen)
                        .Select(segments => string.Join(".", segments)))
                .Where(s => s.Length >= 1 && s.Length <= 200);

            return Arb.From(keyGen.Select(s => new ValidPermissionKey(s)));
        }
    }

    public static class InvalidPermissionKeyArbitrary
    {
        public static Arbitrary<InvalidPermissionKey> InvalidPermissionKeyArb()
        {
            var generators = new[]
            {
                // Uppercase letters
                Gen.Constant("Settings.Read"),
                Gen.Constant("SETTINGS.READ"),
                Gen.Constant("settings.Read"),
                // Starts with digit
                Gen.Constant("1settings.read"),
                Gen.Constant("settings.1read"),
                // Consecutive dots
                Gen.Constant("settings..read"),
                Gen.Constant("settings...read"),
                // Leading/trailing dots
                Gen.Constant(".settings.read"),
                Gen.Constant("settings.read."),
                // Invalid characters
                Gen.Constant("settings-read"),
                Gen.Constant("settings_read"),
                Gen.Constant("settings read"),
                Gen.Constant("settings/read"),
                Gen.Constant("settings@read"),
                // Empty string
                Gen.Constant("")
            };

            var combined = Gen.OneOf(generators).Select(s => new InvalidPermissionKey(s));
            return Arb.From(combined);
        }
    }
}
