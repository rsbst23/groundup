using System.Globalization;
using System.Text.Json;
using GroundUp.Core.Enums;
using GroundUp.Core.Results;

namespace GroundUp.Services.Settings;

/// <summary>
/// Internal helper responsible for converting string setting values to typed CLR objects
/// based on the <see cref="SettingDataType"/> enum. Handles single values and JSON arrays
/// when <c>AllowMultiple</c> is true.
/// </summary>
internal static class SettingValueConverter
{
    /// <summary>
    /// Converts a string value to the requested CLR type based on the setting's data type.
    /// </summary>
    /// <typeparam name="T">The target CLR type to convert to.</typeparam>
    /// <param name="value">The raw string value from the database (may be null).</param>
    /// <param name="dataType">The declared data type of the setting definition.</param>
    /// <param name="allowMultiple">When true, the value is deserialized as a JSON array.</param>
    /// <param name="settingKey">The setting key, used in error messages.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the typed value on success, or a failure message on parse error.</returns>
    public static OperationResult<T> Convert<T>(string? value, SettingDataType dataType, bool allowMultiple, string settingKey)
    {
        if (value is null)
        {
            return OperationResult<T>.Ok(default!);
        }

        if (allowMultiple)
        {
            return DeserializeJsonArray<T>(value, settingKey);
        }

        return dataType switch
        {
            SettingDataType.String => ConvertString<T>(value, settingKey),
            SettingDataType.Int => ConvertInt<T>(value, settingKey),
            SettingDataType.Long => ConvertLong<T>(value, settingKey),
            SettingDataType.Decimal => ConvertDecimal<T>(value, settingKey),
            SettingDataType.Bool => ConvertBool<T>(value, settingKey),
            SettingDataType.DateTime => ConvertDateTime<T>(value, settingKey),
            SettingDataType.Date => ConvertDate<T>(value, settingKey),
            SettingDataType.Json => DeserializeJson<T>(value, settingKey),
            _ => OperationResult<T>.Fail(
                $"Unsupported data type '{dataType}' for setting '{settingKey}'",
                400)
        };
    }

    private static OperationResult<T> ConvertString<T>(string value, string settingKey)
    {
        if (value is T typed)
        {
            return OperationResult<T>.Ok(typed);
        }

        return OperationResult<T>.Fail(
            $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
            400);
    }

    private static OperationResult<T> ConvertInt<T>(string value, string settingKey)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            if (parsed is T typed)
            {
                return OperationResult<T>.Ok(typed);
            }
        }

        return OperationResult<T>.Fail(
            $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
            400);
    }

    private static OperationResult<T> ConvertLong<T>(string value, string settingKey)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            if (parsed is T typed)
            {
                return OperationResult<T>.Ok(typed);
            }
        }

        return OperationResult<T>.Fail(
            $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
            400);
    }

    private static OperationResult<T> ConvertDecimal<T>(string value, string settingKey)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            if (parsed is T typed)
            {
                return OperationResult<T>.Ok(typed);
            }
        }

        return OperationResult<T>.Fail(
            $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
            400);
    }

    private static OperationResult<T> ConvertBool<T>(string value, string settingKey)
    {
        if (bool.TryParse(value, out var parsed))
        {
            if (parsed is T typed)
            {
                return OperationResult<T>.Ok(typed);
            }
        }

        return OperationResult<T>.Fail(
            $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
            400);
    }

    private static OperationResult<T> ConvertDateTime<T>(string value, string settingKey)
    {
        if (System.DateTime.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            if (parsed is T typed)
            {
                return OperationResult<T>.Ok(typed);
            }
        }

        return OperationResult<T>.Fail(
            $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
            400);
    }

    private static OperationResult<T> ConvertDate<T>(string value, string settingKey)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            if (parsed is T typed)
            {
                return OperationResult<T>.Ok(typed);
            }
        }

        return OperationResult<T>.Fail(
            $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
            400);
    }

    private static OperationResult<T> DeserializeJson<T>(string value, string settingKey)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(value);
            return OperationResult<T>.Ok(result!);
        }
        catch (JsonException)
        {
            return OperationResult<T>.Fail(
                $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
                400);
        }
    }

    private static OperationResult<T> DeserializeJsonArray<T>(string value, string settingKey)
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(value);
            return OperationResult<T>.Ok(result!);
        }
        catch (JsonException)
        {
            return OperationResult<T>.Fail(
                $"Cannot convert '{value}' to {typeof(T).Name} for setting '{settingKey}'",
                400);
        }
    }
}
