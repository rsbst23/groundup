namespace GroundUp.Events;

/// <summary>
/// Published when a setting value is created, updated, or deleted.
/// Carries the setting key, the level and scope where the change occurred,
/// and the old and new values for downstream consumers to react to
/// configuration changes (e.g., invalidate caches, update runtime behavior).
/// </summary>
public sealed record SettingChangedEvent : BaseEvent
{
    /// <summary>
    /// The setting definition key that was changed.
    /// </summary>
    public required string SettingKey { get; init; }

    /// <summary>
    /// The cascade level where the change occurred.
    /// </summary>
    public required Guid LevelId { get; init; }

    /// <summary>
    /// The specific scope entity where the change occurred.
    /// Null for system-level changes.
    /// </summary>
    public Guid? ScopeId { get; init; }

    /// <summary>
    /// The previous value before the change. Null for new creates.
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// The new value after the change. Null for deletes.
    /// </summary>
    public string? NewValue { get; init; }
}
