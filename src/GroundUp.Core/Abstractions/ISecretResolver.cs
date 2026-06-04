namespace GroundUp.Core.Abstractions;

/// <summary>
/// Resolves secret references (values prefixed with "secretref://") from external
/// secret stores such as Azure Key Vault, AWS Secrets Manager, or HSMs.
/// No implementation ships in Phase 10AB — this is the extension point contract.
///
/// Resolution is single-pass: if the resolver returns a value that itself starts
/// with "secretref://", that returned value is treated as the final, literal
/// resolved value and is NOT re-resolved.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Resolves a secret reference to its actual value.
    /// </summary>
    /// <param name="secretRef">The full secret reference string (including the secretref:// prefix).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved secret value, or null if the reference cannot be resolved.</returns>
    Task<string?> ResolveAsync(string secretRef, CancellationToken cancellationToken = default);
}
