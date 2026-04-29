namespace GroundUp.Core.Enums;

/// <summary>
/// Supported data types for setting values. Determines how the string
/// Value column is deserialized into a CLR type.
/// </summary>
public enum SettingDataType
{
    /// <summary>Plain text string (<see cref="string"/>).</summary>
    String = 0,

    /// <summary>32-bit integer (<see cref="int"/>).</summary>
    Int = 1,

    /// <summary>64-bit integer (<see cref="long"/>).</summary>
    Long = 2,

    /// <summary>Decimal number (<see cref="decimal"/>).</summary>
    Decimal = 3,

    /// <summary>Boolean true/false (<see cref="bool"/>).</summary>
    Bool = 4,

    /// <summary>Date and time (<see cref="System.DateTime"/>).</summary>
    DateTime = 5,

    /// <summary>Date only (<see cref="System.DateOnly"/>).</summary>
    Date = 6,

    /// <summary>Arbitrary JSON structure (System.Text.Json).</summary>
    Json = 7
}
