namespace GroundUp.Core.Configuration;

/// <summary>
/// Configuration options for the setup wizard endpoints.
/// Bound from the "GroundUp:Setup" configuration section.
/// </summary>
public sealed class SetupOptions
{
    /// <summary>
    /// The configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "GroundUp:Setup";

    /// <summary>
    /// Maximum allowed request body size in bytes for setup endpoints.
    /// Defaults to 65536 (64 KB). Minimum enforced floor is 4096 bytes.
    /// </summary>
    public int MaxRequestBodyBytes { get; set; } = 65536;

    /// <summary>
    /// Rate limiting options for setup endpoints.
    /// </summary>
    public SetupRateLimitOptions RateLimit { get; set; } = new();
}

/// <summary>
/// Rate limiting configuration for setup wizard endpoints.
/// Bound from the "GroundUp:Setup:RateLimit" configuration section.
/// Uses a fixed-window strategy per remote IP address.
/// </summary>
public sealed class SetupRateLimitOptions
{
    /// <summary>
    /// The configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "GroundUp:Setup:RateLimit";

    /// <summary>
    /// Maximum number of requests allowed per window. Defaults to 30.
    /// </summary>
    public int RequestsPerWindow { get; set; } = 30;

    /// <summary>
    /// Duration of the rate limit window in seconds. Defaults to 60.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Configuration options for the setup transaction log rotation.
/// Bound from the "GroundUp:SetupTransactionLog" configuration section.
/// </summary>
public sealed class SetupTransactionLogOptions
{
    /// <summary>
    /// The configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "GroundUp:SetupTransactionLog";

    /// <summary>
    /// Maximum number of transaction log rows to retain.
    /// Rows exceeding this count are rotated (oldest first), except rows
    /// in a pending stage which are never auto-deleted.
    /// Defaults to 1000.
    /// </summary>
    public int MaxRowCount { get; set; } = 1000;
}
