using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Xunit;
using GroundUp.Auth.Services;

namespace GroundUp.Tests.Unit.Auth;

/// <summary>
/// Property-based tests for <see cref="SlugGenerator"/>.
/// Feature: phase-10c-auth-dispatcher, Property 10: Slug Generation Validity and Disambiguation.
/// Validates: Requirements 7.4, 7.5
/// </summary>
[Trait("Category", "Property")]
public sealed class SlugGeneratorPropertyTests
{
    private static readonly Regex ValidSlugRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);

    // --- Property 10: Slug Generation Validity ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 10: Slug Generation Validity and Disambiguation
    /// For any non-null string, the derived slug SHALL contain only lowercase alphanumeric characters and hyphens.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SlugGeneratorArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 10: FromOrganizationName produces only valid characters [a-z0-9-]")]
    public Property FromOrganizationName_ContainsOnlyValidCharacters(OrganizationNameInput input)
    {
        var slug = SlugGenerator.FromOrganizationName(input.Value);

        return ValidSlugRegex.IsMatch(slug).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 10: Slug Generation Validity and Disambiguation
    /// For any non-null string, the derived slug SHALL NOT start with a hyphen.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SlugGeneratorArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 10: FromOrganizationName does not start with hyphen")]
    public Property FromOrganizationName_DoesNotStartWithHyphen(OrganizationNameInput input)
    {
        var slug = SlugGenerator.FromOrganizationName(input.Value);

        return (!slug.StartsWith('-')).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 10: Slug Generation Validity and Disambiguation
    /// For any non-null string, the derived slug SHALL NOT end with a hyphen.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SlugGeneratorArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 10: FromOrganizationName does not end with hyphen")]
    public Property FromOrganizationName_DoesNotEndWithHyphen(OrganizationNameInput input)
    {
        var slug = SlugGenerator.FromOrganizationName(input.Value);

        return (!slug.EndsWith('-')).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 10: Slug Generation Validity and Disambiguation
    /// For any input (including empty, whitespace, special chars only), the output is never empty.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SlugGeneratorArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 10: FromOrganizationName is never empty")]
    public Property FromOrganizationName_IsNeverEmpty(OrganizationNameInput input)
    {
        var slug = SlugGenerator.FromOrganizationName(input.Value);

        return (!string.IsNullOrEmpty(slug)).ToProperty();
    }

    // --- Property 10: Disambiguation ---

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 10: Slug Generation Validity and Disambiguation
    /// For any valid base slug and attempt >= 2, the disambiguated slug SHALL be in the format {baseSlug}-{attempt}
    /// and still valid per the same character rules.
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SlugGeneratorArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 10: Disambiguate produces valid format {baseSlug}-{attempt}")]
    public Property Disambiguate_ProducesValidFormat(ValidBaseSlug baseSlug, DisambiguationAttempt attempt)
    {
        var result = SlugGenerator.Disambiguate(baseSlug.Value, attempt.Value);

        var expectedFormat = $"{baseSlug.Value}-{attempt.Value}";
        var isValidFormat = result == expectedFormat;
        var containsOnlyValidChars = ValidSlugRegex.IsMatch(result);
        var doesNotStartWithHyphen = !result.StartsWith('-');
        var doesNotEndWithHyphen = !result.EndsWith('-');
        var isNotEmpty = !string.IsNullOrEmpty(result);

        return (isValidFormat && containsOnlyValidChars && doesNotStartWithHyphen && doesNotEndWithHyphen && isNotEmpty)
            .ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 10: Slug Generation Validity and Disambiguation
    /// For any base slug and attempt >= 2, the disambiguated slug SHALL differ from the original base slug.
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SlugGeneratorArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 10: Disambiguate differs from original")]
    public Property Disambiguate_DiffersFromOriginal(ValidBaseSlug baseSlug, DisambiguationAttempt attempt)
    {
        var result = SlugGenerator.Disambiguate(baseSlug.Value, attempt.Value);

        return (result != baseSlug.Value).ToProperty();
    }

    /// <summary>
    /// Feature: phase-10c-auth-dispatcher, Property 10: Slug Generation Validity and Disambiguation
    /// For the same slug + attempt, the result is always the same (deterministic).
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(SlugGeneratorArbitraries) },
        DisplayName = "Feature: phase-10c-auth-dispatcher, Property 10: Disambiguate is deterministic")]
    public Property Disambiguate_IsDeterministic(ValidBaseSlug baseSlug, DisambiguationAttempt attempt)
    {
        var result1 = SlugGenerator.Disambiguate(baseSlug.Value, attempt.Value);
        var result2 = SlugGenerator.Disambiguate(baseSlug.Value, attempt.Value);

        return (result1 == result2).ToProperty();
    }
}

// --- Test data types ---

/// <summary>
/// Represents an organization name input for testing slug generation.
/// Wraps a non-null string that may include unicode, special chars, whitespace, empty, or very long strings.
/// </summary>
public sealed record OrganizationNameInput(string Value)
{
    public override string ToString() => $"\"{Value}\"";
}

/// <summary>
/// Represents a valid base slug for Disambiguate testing (non-empty, only [a-z0-9-], no leading/trailing hyphens).
/// </summary>
public sealed record ValidBaseSlug(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Represents a valid disambiguation attempt number (>= 2).
/// </summary>
public sealed record DisambiguationAttempt(int Value)
{
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Custom FsCheck Arbitrary generators for SlugGenerator property tests.
/// </summary>
public static class SlugGeneratorArbitraries
{
    /// <summary>
    /// Generates arbitrary organization name inputs including unicode, special chars, spaces, and very long strings.
    /// </summary>
    public static Arbitrary<OrganizationNameInput> OrganizationNameInputArb()
    {
        var gen = Gen.OneOf(
            // Standard FsCheck string generation (includes unicode, special chars)
            Arb.Default.NonNull<string>().Generator.Select(nn => nn.Get),
            // Empty string
            Gen.Constant(string.Empty),
            // Whitespace variants
            Gen.Elements(" ", "  ", "\t", "\n", " \t\n "),
            // Special characters only
            Gen.Elements("!!!@@@###", "---", "....", "***", "___", "☺☻♥♦♣♠"),
            // Unicode organization names
            Gen.Elements("Ünternehmen GmbH", "日本語テスト", "Société Française", "北京公司", "مؤسسة عربية"),
            // Very long strings
            Gen.Choose(100, 500).Select(len => new string('a', len)),
            // Mixed content
            Gen.Elements("Acme Corp.", "My-Company 123", "Hello   World", " leading", "trailing ", "-hyphens-")
        ).Select(s => new OrganizationNameInput(s));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates valid base slugs (lowercase alphanumeric + hyphens, no leading/trailing hyphens, non-empty).
    /// </summary>
    public static Arbitrary<ValidBaseSlug> ValidBaseSlugArb()
    {
        // Generate slug-valid characters
        var slugCharGen = Gen.OneOf(
            Gen.Choose('a', 'z').Select(c => (char)c),
            Gen.Choose('0', '9').Select(c => (char)c),
            Gen.Constant('-')
        );

        var gen = Gen.Choose(1, 30).SelectMany(length =>
            Gen.ArrayOf(length, slugCharGen).Select(chars => new string(chars)))
            // Ensure no leading/trailing hyphens and non-empty after trimming
            .Select(s => s.Trim('-'))
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => new ValidBaseSlug(s));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates valid disambiguation attempt numbers (>= 2, reasonable upper bound).
    /// </summary>
    public static Arbitrary<DisambiguationAttempt> DisambiguationAttemptArb()
    {
        var gen = Gen.Choose(2, 1000).Select(n => new DisambiguationAttempt(n));
        return gen.ToArbitrary();
    }
}
