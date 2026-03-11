using System.Globalization;
using System.Text.Json;
using Requestr.Core.Models;

namespace Requestr.Core.Utilities;

/// <summary>
/// Centralised type conversion utility that converts values to the appropriate CLR type
/// based on the target SQL Server column data type. Replaces multiple ad-hoc converters
/// scattered across the codebase.
/// </summary>
public static class SqlTypeConverter
{
    /// <summary>
    /// Converts a full dictionary of field values using the form field definitions
    /// to determine the target SQL type for each column.
    /// </summary>
    public static Dictionary<string, object?> ConvertDictionary(
        Dictionary<string, object?> data,
        IReadOnlyList<FormField> fields)
    {
        var result = new Dictionary<string, object?>(data.Count);

        foreach (var kvp in data)
        {
            var field = fields.FirstOrDefault(f =>
                string.Equals(f.Name, kvp.Key, StringComparison.OrdinalIgnoreCase));

            result[kvp.Key] = field != null
                ? ConvertToSqlType(kvp.Value, field.SqlDataType)
                : UnwrapJsonElement(kvp.Value);
        }

        return result;
    }

    /// <summary>
    /// Converts a single value to the appropriate CLR type based on the target SQL Server data type.
    /// Falls back to simple unwrapping when sqlDataType is null or unrecognised.
    /// </summary>
    public static object? ConvertToSqlType(object? value, string? sqlDataType)
    {
        if (value is null or DBNull)
            return null;

        // Unwrap JsonElement first
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Null || jsonElement.ValueKind == JsonValueKind.Undefined)
                return null;

            return ConvertJsonElementToSqlType(jsonElement, sqlDataType);
        }

        // Already a CLR value (e.g. from Excel parsing) — convert the string representation if needed
        if (value is string stringValue && !string.IsNullOrEmpty(sqlDataType))
        {
            return ConvertStringToSqlType(stringValue, sqlDataType);
        }

        return value;
    }

    /// <summary>
    /// Simple JsonElement unwrapper — extracts the value without interpreting strings.
    /// Used when no schema information is available.
    /// </summary>
    public static object? UnwrapJsonElement(object? value)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Parses a boolean from common string representations (yes/no, true/false, 1/0).
    /// Used for SQL bit columns.
    /// </summary>
    public static bool ParseBoolean(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            "TRUE" or "YES" or "1" => true,
            "FALSE" or "NO" or "0" => false,
            _ => bool.Parse(value)
        };
    }

    /// <summary>
    /// Parses a date value, handling both OLE Automation numbers (from Excel) and string formats.
    /// </summary>
    public static DateTime ParseDateTime(string value)
    {
        // Try OLE Automation date first (numeric value from Excel — days since Dec 30, 1899)
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var oaDate))
        {
            return DateTime.FromOADate(oaDate);
        }

        return DateTime.Parse(value, CultureInfo.InvariantCulture);
    }

    #region Private helpers

    private static object? ConvertJsonElementToSqlType(JsonElement element, string? sqlDataType)
    {
        if (string.IsNullOrEmpty(sqlDataType))
            return UnwrapJsonElementCore(element);

        var normalised = NormaliseSqlType(sqlDataType);

        return normalised switch
        {
            // Integer types
            "int" or "smallint" or "tinyint" => ConvertToInt32(element),
            "bigint" => ConvertToInt64(element),

            // Boolean
            "bit" => ConvertToBool(element),

            // Exact numeric
            "decimal" or "numeric" or "money" or "smallmoney" => ConvertToDecimal(element),

            // Approximate numeric
            "float" or "real" => ConvertToDouble(element),

            // Date / time
            "datetime" or "datetime2" or "smalldatetime" or "date" => ConvertToDateTime(element),
            "datetimeoffset" => ConvertToDateTimeOffset(element),
            "time" => ConvertToTimeSpan(element),

            // GUID
            "uniqueidentifier" => ConvertToGuid(element),

            // String types — never reinterpret
            "nvarchar" or "varchar" or "nchar" or "char" or "text" or "ntext"
                or "xml" => element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.ToString(),

            // Binary — pass through as string (hex / base64)
            "varbinary" or "binary" or "image" => element.GetString(),

            // Unknown type — safe fallback
            _ => UnwrapJsonElementCore(element)
        };
    }

    private static object? ConvertStringToSqlType(string value, string sqlDataType)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var normalised = NormaliseSqlType(sqlDataType);

        return normalised switch
        {
            "int" or "smallint" or "tinyint"
                => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var i)
                    ? i
                    : TryParseBooleanToInt(value, out var boolInt) ? boolInt : (object)value,

            "bigint"
                => long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var l)
                    ? l
                    : TryParseBooleanToLong(value, out var boolLong) ? boolLong : (object)value,

            "bit" => ParseBoolean(value),

            "decimal" or "numeric" or "money" or "smallmoney"
                => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (object)value,

            "float" or "real"
                => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl) ? dbl : (object)value,

            "datetime" or "datetime2" or "smalldatetime" or "date" => ParseDateTime(value),

            "datetimeoffset"
                => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                    ? dto : (object)value,

            "time"
                => TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var ts) ? ts : (object)value,

            "uniqueidentifier"
                => Guid.TryParse(value, out var g) ? g : (object)value,

            // String types and unknown — return as-is
            _ => value
        };
    }

    /// <summary>
    /// Strips length/precision specifiers and lowercases: "nvarchar(100)" → "nvarchar", "DECIMAL(18,2)" → "decimal"
    /// </summary>
    private static string NormaliseSqlType(string sqlDataType)
    {
        var idx = sqlDataType.IndexOf('(');
        var baseName = idx >= 0 ? sqlDataType[..idx] : sqlDataType;
        return baseName.Trim().ToLowerInvariant();
    }

    private static object? UnwrapJsonElementCore(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    private static object? ConvertToInt32(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt32(out var i) ? i : (int)element.GetDouble(),
        JsonValueKind.String => int.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var i) ? i : (object?)element.GetString(),
        JsonValueKind.True => 1,
        JsonValueKind.False => 0,
        _ => element.ToString()
    };

    private static object? ConvertToInt64(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : (long)element.GetDouble(),
        JsonValueKind.String => long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : (object?)element.GetString(),
        JsonValueKind.True => 1L,
        JsonValueKind.False => 0L,
        _ => element.ToString()
    };

    private static object? ConvertToBool(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.GetInt32() != 0,
        JsonValueKind.String => ParseBoolean(element.GetString() ?? "false"),
        _ => element.ToString()
    };

    private static object? ConvertToDecimal(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDecimal(),
        JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (object?)element.GetString(),
        _ => element.ToString()
    };

    private static object? ConvertToDouble(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (object?)element.GetString(),
        _ => element.ToString()
    };

    private static object? ConvertToDateTime(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : (object?)element.GetString(),
        JsonValueKind.Number => DateTime.FromOADate(element.GetDouble()), // OLE Automation date
        _ => element.ToString()
    };

    private static object? ConvertToDateTimeOffset(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto) ? dto : (object?)element.GetString(),
        _ => element.ToString()
    };

    private static object? ConvertToTimeSpan(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => TimeSpan.TryParse(element.GetString(), CultureInfo.InvariantCulture, out var ts) ? ts : (object?)element.GetString(),
        _ => element.ToString()
    };

    private static object? ConvertToGuid(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => Guid.TryParse(element.GetString(), out var g) ? g : (object?)element.GetString(),
        _ => element.ToString()
    };

    /// <summary>
    /// Attempts to parse a boolean string (TRUE/FALSE/YES/NO) and return it as an int (1/0).
    /// Used when Excel provides boolean text for an integer SQL column.
    /// </summary>
    private static bool TryParseBooleanToInt(string value, out int result)
    {
        var normalized = value.Trim().ToUpperInvariant();
        switch (normalized)
        {
            case "TRUE" or "YES":
                result = 1;
                return true;
            case "FALSE" or "NO":
                result = 0;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>
    /// Attempts to parse a boolean string (TRUE/FALSE/YES/NO) and return it as a long (1/0).
    /// Used when Excel provides boolean text for a bigint SQL column.
    /// </summary>
    private static bool TryParseBooleanToLong(string value, out long result)
    {
        var normalized = value.Trim().ToUpperInvariant();
        switch (normalized)
        {
            case "TRUE" or "YES":
                result = 1L;
                return true;
            case "FALSE" or "NO":
                result = 0L;
                return true;
            default:
                result = 0L;
                return false;
        }
    }

    #endregion
}
