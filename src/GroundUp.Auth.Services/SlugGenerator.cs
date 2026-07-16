using System.Text.RegularExpressions;

namespace GroundUp.Auth.Services;

/// <summary>
/// Utility for deriving URL-friendly tenant slugs from organization names
/// and handling disambiguation on collision.
/// </summary>
public static partial class SlugGenerator
{
    /// <summary>
    /// Fallback slug used when the derived slug would otherwise be empty
    /// (e.g., the organization name contains only non-alphanumeric characters).
    /// </summary>
    private const string FallbackSlug = "org";

    /// <summary>
    /// Derives a URL-friendly slug from an organization name.
    /// The slug contains only lowercase alphanumeric characters and hyphens,
    /// does not start or end with a hyphen, and is never empty.
    /// </summary>
    /// <param name="organizationName">The organization name to derive a slug from.</param>
    /// <returns>A valid, non-empty slug string.</returns>
    public static string FromOrganizationName(string organizationName)
    {
        if (string.IsNullOrWhiteSpace(organizationName))
        {
            return FallbackSlug;
        }

        // Lowercase the input
        var slug = organizationName.ToLowerInvariant();

        // Replace any sequence of non-alphanumeric characters (except hyphens) with a single hyphen
        slug = NonAlphanumericPattern().Replace(slug, "-");

        // Collapse consecutive hyphens to a single hyphen
        slug = ConsecutiveHyphensPattern().Replace(slug, "-");

        // Trim leading and trailing hyphens
        slug = slug.Trim('-');

        // If the result is empty after processing, use the fallback
        if (string.IsNullOrEmpty(slug))
        {
            return FallbackSlug;
        }

        return slug;
    }

    /// <summary>
    /// Appends a numeric suffix to disambiguate a colliding slug.
    /// The disambiguated slug remains valid per the same character rules
    /// (lowercase alphanumeric + hyphens, no leading/trailing hyphens, non-empty).
    /// </summary>
    /// <param name="baseSlug">The original slug that collided.</param>
    /// <param name="attempt">The attempt number (must be >= 2). The suffix appended is this value.</param>
    /// <returns>A disambiguated slug in the form <c>{baseSlug}-{attempt}</c>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="baseSlug"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="attempt"/> is less than 2.</exception>
    public static string Disambiguate(string baseSlug, int attempt)
    {
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            throw new ArgumentException("Base slug must not be null, empty, or whitespace.", nameof(baseSlug));
        }

        if (attempt < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt must be >= 2.");
        }

        return $"{baseSlug}-{attempt}";
    }

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex NonAlphanumericPattern();

    [GeneratedRegex("-{2,}")]
    private static partial Regex ConsecutiveHyphensPattern();
}
