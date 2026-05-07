using FsCheck;
using FsCheck.Xunit;
using FluentValidation.Results;
using GroundUp.Auth.Core.Dtos;
using GroundUp.Auth.Core.Enums;
using GroundUp.Auth.Core.Validators;

namespace GroundUp.Tests.Unit.Authentication;

/// <summary>
/// Property-based tests for tenant slug validation.
/// Feature: phase-9a-auth-entities, Property 2: Tenant slug validation
/// Validates: Requirements 18.1, 18.2
/// </summary>
public sealed class TenantSlugValidatorPropertyTests
{
    private static readonly CreateTenantDtoValidator CreateValidator = new();
    private static readonly UpdateTenantDtoValidator UpdateValidator = new();

    /// <summary>
    /// Property 2: Valid slugs produce zero validation errors on the Slug field.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidSlugArbitrary) })]
    public Property CreateTenantDto_ValidSlug_ProducesNoErrors(ValidSlug slug)
    {
        var dto = new CreateTenantDto(
            Name: "Valid Tenant",
            Slug: slug.Value,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: null,
            CustomDomain: null);

        var result = CreateValidator.Validate(dto);

        return result.IsValid.ToProperty()
            .Label($"Slug '{slug.Value}' should be valid");
    }

    /// <summary>
    /// Property 2: Valid slugs produce zero validation errors on UpdateTenantDtoValidator.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidSlugArbitrary) })]
    public Property UpdateTenantDto_ValidSlug_ProducesNoErrors(ValidSlug slug)
    {
        var dto = new UpdateTenantDto(
            Name: "Valid Tenant",
            Slug: slug.Value,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            RealmName: null,
            CustomDomain: null,
            IsActive: true);

        var result = UpdateValidator.Validate(dto);

        return result.IsValid.ToProperty()
            .Label($"Slug '{slug.Value}' should be valid");
    }

    /// <summary>
    /// Property 2: Invalid slugs produce at least one error on the Slug field.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InvalidSlugArbitrary) })]
    public Property CreateTenantDto_InvalidSlug_ProducesSlugError(InvalidSlug slug)
    {
        var dto = new CreateTenantDto(
            Name: "Valid Tenant",
            Slug: slug.Value,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            ParentTenantId: null,
            RealmName: null,
            CustomDomain: null);

        var result = CreateValidator.Validate(dto);
        var slugErrors = result.Errors.Where(e => e.PropertyName == "Slug").ToList();

        return (slugErrors.Count > 0).ToProperty()
            .Label($"Slug '{slug.Value}' should be invalid");
    }

    /// <summary>
    /// Property 2: Invalid slugs produce at least one error on UpdateTenantDtoValidator.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InvalidSlugArbitrary) })]
    public Property UpdateTenantDto_InvalidSlug_ProducesSlugError(InvalidSlug slug)
    {
        var dto = new UpdateTenantDto(
            Name: "Valid Tenant",
            Slug: slug.Value,
            TenantType: TenantType.Standard,
            OnboardingMode: OnboardingMode.InviteOnly,
            RealmName: null,
            CustomDomain: null,
            IsActive: true);

        var result = UpdateValidator.Validate(dto);
        var slugErrors = result.Errors.Where(e => e.PropertyName == "Slug").ToList();

        return (slugErrors.Count > 0).ToProperty()
            .Label($"Slug '{slug.Value}' should be invalid");
    }

    // --- Custom types and generators ---

    public record ValidSlug(string Value);
    public record InvalidSlug(string Value);

    public static class ValidSlugArbitrary
    {
        private static readonly char[] SlugChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        public static Arbitrary<ValidSlug> ValidSlugArb()
        {
            // Generate a valid slug: one or more segments of [a-z0-9]+ separated by single hyphens
            var segmentGen = Gen.Choose(1, 10)
                .SelectMany(len =>
                    Gen.ArrayOf(len, Gen.Elements(SlugChars))
                        .Select(chars => new string(chars)));

            var slugGen = Gen.Choose(1, 5)
                .SelectMany(segCount =>
                    Gen.ArrayOf(segCount, segmentGen)
                        .Select(segments => string.Join("-", segments)))
                .Where(s => s.Length >= 1 && s.Length <= 100);

            return Arb.From(slugGen.Select(s => new ValidSlug(s)));
        }
    }

    public static class InvalidSlugArbitrary
    {
        public static Arbitrary<InvalidSlug> InvalidSlugArb()
        {
            var generators = new[]
            {
                // Uppercase letters
                Gen.Constant("My-Slug"),
                Gen.Constant("UPPERCASE"),
                Gen.Constant("mixedCase"),
                // Spaces
                Gen.Constant("has space"),
                Gen.Constant(" leading"),
                // Consecutive hyphens
                Gen.Constant("double--hyphen"),
                Gen.Constant("triple---hyphen"),
                // Leading/trailing hyphens
                Gen.Constant("-leading"),
                Gen.Constant("trailing-"),
                Gen.Constant("-both-"),
                // Special characters
                Gen.Constant("special@char"),
                Gen.Constant("under_score"),
                Gen.Constant("dot.notation"),
                Gen.Constant("slash/path"),
                // Empty string
                Gen.Constant("")
            };

            var combined = Gen.OneOf(generators).Select(s => new InvalidSlug(s));
            return Arb.From(combined);
        }
    }
}
