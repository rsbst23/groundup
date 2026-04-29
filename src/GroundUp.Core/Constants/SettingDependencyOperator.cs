namespace GroundUp.Core.Constants;

/// <summary>
/// String constants for conditional dependency operators used in
/// <c>SettingDefinition.DependsOnOperator</c>. Avoids magic strings
/// when defining conditional visibility or validation rules between settings.
/// </summary>
public static class SettingDependencyOperator
{
    /// <summary>
    /// The dependent setting's value must exactly equal the specified value.
    /// </summary>
    public new const string Equals = "Equals";

    /// <summary>
    /// The dependent setting's value must not equal the specified value.
    /// </summary>
    public const string NotEquals = "NotEquals";

    /// <summary>
    /// The dependent setting's value must contain the specified substring.
    /// </summary>
    public const string Contains = "Contains";

    /// <summary>
    /// The dependent setting's value must be one of the specified comma-separated values.
    /// </summary>
    public const string In = "In";
}
