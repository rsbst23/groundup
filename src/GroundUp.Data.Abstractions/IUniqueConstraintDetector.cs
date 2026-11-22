namespace GroundUp.Data.Abstractions;

/// <summary>
/// Tells whether a failed save was caused by a unique-constraint violation. Lets repositories and
/// services react to duplicates without provider-specific code: each database provider (for example
/// <c>GroundUp.Data.Postgres</c>) registers its own implementation.
/// </summary>
public interface IUniqueConstraintDetector
{
    /// <summary>
    /// Returns true when <paramref name="exception"/> (typically EF Core's <c>DbUpdateException</c>)
    /// was caused by a unique-constraint violation.
    /// </summary>
    /// <param name="exception">The exception thrown by the save.</param>
    /// <returns>True for a unique-constraint violation.</returns>
    bool IsUniqueConstraintViolation(Exception exception);
}
