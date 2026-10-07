using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;

namespace Requestr.Core.Utilities;

/// <summary>
/// Builds the canonical target-record key stored on requests: a JSON object of primary key values
/// in key order, e.g. {"Id":"123"}. Every writer and reader must use this so keys compare equal.
/// </summary>
public static partial class RecordKeyBuilder
{
    public const int MaxLength = 450;

    public static string? Build(IReadOnlyList<string> primaryKeyColumns, IReadOnlyDictionary<string, object?> values)
    {
        if (primaryKeyColumns.Count == 0)
            return null;

        var parts = new List<KeyValuePair<string, string>>(primaryKeyColumns.Count);
        foreach (var column in primaryKeyColumns)
        {
            var value = Find(values, column);
            var normalized = Normalize(value);
            if (string.IsNullOrEmpty(normalized))
                return null;
            parts.Add(new(column, normalized));
        }

        var key = Serialize(parts);
        return key.Length <= MaxLength ? key : null;
    }

    /// <summary>Inserts are identified by their submitted/generated values; updates and deletes by the original row.</summary>
    public static string? ForRequest(IReadOnlyList<string> primaryKeyColumns, RequestType requestType,
        IReadOnlyDictionary<string, object?> fieldValues, IReadOnlyDictionary<string, object?> originalValues) =>
        Build(primaryKeyColumns, requestType == RequestType.Insert ? fieldValues : originalValues);

    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? recordKey)
    {
        if (string.IsNullOrWhiteSpace(recordKey))
            return [];
        try
        {
            using var document = JsonDocument.Parse(recordKey);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [];
            return document.RootElement.EnumerateObject()
                .Select(property => new KeyValuePair<string, string>(property.Name, property.Value.GetString() ?? string.Empty))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Human-readable form of a key, e.g. "Id = 123".</summary>
    public static string Describe(string? recordKey)
    {
        var parts = Parse(recordKey);
        return parts.Count == 0 ? recordKey ?? string.Empty : string.Join(", ", parts.Select(part => $"{part.Key} = {part.Value}"));
    }

    /// <summary>
    /// Recovers an insert key from the pre-RecordKey AppliedRecordKey value, which held the raw inserted id
    /// either as a plain value or as a Dapper row string such as "{DapperRow, Id = '123'}".
    /// </summary>
    public static string? FromLegacyAppliedKey(string? legacyKey, IReadOnlyList<string> primaryKeyColumns)
    {
        if (string.IsNullOrWhiteSpace(legacyKey) || primaryKeyColumns.Count != 1)
            return null;

        var column = primaryKeyColumns[0];
        var dapperRow = LegacyDapperRow().Match(legacyKey);
        if (dapperRow.Success)
        {
            return string.Equals(dapperRow.Groups["column"].Value, column, StringComparison.OrdinalIgnoreCase)
                ? Build(primaryKeyColumns, new Dictionary<string, object?> { [column] = dapperRow.Groups["value"].Value })
                : null;
        }

        // Update/delete legacy keys were "Column=Value" pairs; those requests are keyed from OriginalValues instead.
        return legacyKey.Contains('=') ? null : Build(primaryKeyColumns, new Dictionary<string, object?> { [column] = legacyKey });
    }

    /// <summary>Writes a generated identity value into the inserted values so the new record can be keyed.</summary>
    public static void ApplyInsertedIdentity(Dictionary<string, object?> values, object? insertedId, string? identityColumn)
    {
        if (string.IsNullOrEmpty(identityColumn))
            return;
        var id = insertedId is IDictionary<string, object> row ? row.Values.FirstOrDefault() : insertedId;
        if (id != null)
            values[identityColumn] = id is IConvertible ? Convert.ToInt64(id, CultureInfo.InvariantCulture) : id;
    }

    private static object? Find(IReadOnlyDictionary<string, object?> values, string column)
    {
        if (values.TryGetValue(column, out var exact))
            return exact;
        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, column, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }
        return null;
    }

    private static string? Normalize(object? value) => value switch
    {
        null or DBNull => null,
        JsonElement element => NormalizeJson(element),
        string text => text.TrimEnd(),
        Guid guid => guid.ToString("D"),
        bool flag => flag ? "1" : "0",
        DateTime dateTime => dateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToHexString(bytes),
        decimal number => NormalizeNumber(number),
        double number => NormalizeFloatingPoint(number),
        float number => NormalizeFloatingPoint(number),
        IConvertible convertible when IsInteger(convertible) => convertible.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()?.TrimEnd()
    };

    private static string? NormalizeJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()?.TrimEnd(),
        JsonValueKind.Number => element.TryGetDecimal(out var number) ? NormalizeNumber(number) : element.GetRawText(),
        JsonValueKind.True => "1",
        JsonValueKind.False => "0",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText()
    };

    private static string NormalizeNumber(decimal number) => number.ToString("G29", CultureInfo.InvariantCulture);

    private static string NormalizeFloatingPoint(double number) =>
        number is >= (double)decimal.MinValue and <= (double)decimal.MaxValue
            ? NormalizeNumber((decimal)number)
            : number.ToString("R", CultureInfo.InvariantCulture);

    private static bool IsInteger(IConvertible value) => value.GetTypeCode() is TypeCode.Byte or TypeCode.SByte
        or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;

    private static string Serialize(List<KeyValuePair<string, string>> parts)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var part in parts)
                writer.WriteString(part.Key, part.Value);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    [GeneratedRegex(@"^\{DapperRow, (?<column>[^=]+?) = '(?<value>.*)'\}$")]
    private static partial Regex LegacyDapperRow();
}

public static class RecordKeyDataServiceExtensions
{
    /// <summary>
    /// Builds a record key using the target table's primary key. Never throws: a missing key must not fail
    /// a submission or a change that has already been written to the target database.
    /// </summary>
    public static async Task<string?> TryBuildRecordKeyAsync(this IDataService dataService, string databaseConnectionName,
        string tableName, string schema, RequestType requestType, IReadOnlyDictionary<string, object?> fieldValues,
        IReadOnlyDictionary<string, object?> originalValues, ILogger logger)
    {
        try
        {
            var primaryKeyColumns = await dataService.GetPrimaryKeyColumnsAsync(databaseConnectionName, tableName, schema);
            return RecordKeyBuilder.ForRequest(primaryKeyColumns, requestType, fieldValues, originalValues);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not build record key for {Schema}.{TableName} on {Connection}", schema, tableName, databaseConnectionName);
            return null;
        }
    }
}
