using System.Globalization;
using System.Text.Json;
using Dapper;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;

namespace Requestr.Core.Services;

public sealed class RecordHistoryService(IDbConnectionFactory connectionFactory) : IRecordHistoryService
{
    private const string HistorySql = """
        SELECT fr.Id AS FormRequestId, CAST(NULL AS int) AS BulkFormRequestId, CAST(NULL AS int) AS BulkRowNumber,
               fr.FormDefinitionId, fd.Name AS FormName, fr.RequestType, fr.Status,
               COALESCE(u.DisplayName, fr.RequestedBy) AS RequestedByName, fr.RequestedAt, fr.ApprovedAt,
               fr.FieldValues AS FieldValuesJson, fr.OriginalValues AS OriginalValuesJson
        FROM FormRequests fr
        INNER JOIN FormDefinitions fd ON fd.Id = fr.FormDefinitionId
        LEFT JOIN Users u ON u.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
        WHERE fr.RecordKey = @RecordKey
          AND fr.BulkFormRequestId IS NULL
          AND fd.DatabaseConnectionName = @DatabaseConnectionName AND fd.[Schema] = @Schema AND fd.TableName = @TableName
        UNION ALL
        SELECT NULL, b.Id, i.RowNumber,
               b.FormDefinitionId, fd.Name, b.RequestType,
               CASE WHEN b.Status IN (@Rejected, @Cancelled) THEN b.Status ELSE i.Status END,
               COALESCE(u.DisplayName, b.RequestedBy), b.RequestedAt, b.ApprovedAt,
               i.FieldValues, i.OriginalValues
        FROM BulkFormRequestItems i
        INNER JOIN BulkFormRequests b ON b.Id = i.BulkFormRequestId
        INNER JOIN FormDefinitions fd ON fd.Id = b.FormDefinitionId
        LEFT JOIN Users u ON u.UserObjectId = TRY_CONVERT(uniqueidentifier, b.RequestedBy)
        WHERE i.RecordKey = @RecordKey
          AND fd.DatabaseConnectionName = @DatabaseConnectionName AND fd.[Schema] = @Schema AND fd.TableName = @TableName
        ORDER BY RequestedAt DESC
        """;

    public async Task<List<RecordHistoryEntry>> GetHistoryAsync(FormDefinition form, string recordKey)
    {
        using var connection = await connectionFactory.CreateConnectionAsync();
        var rows = await connection.QueryAsync<HistoryRow>(HistorySql, new
        {
            RecordKey = recordKey,
            form.DatabaseConnectionName,
            form.Schema,
            form.TableName,
            Rejected = (int)RequestStatus.Rejected,
            Cancelled = (int)RequestStatus.Cancelled
        }, commandTimeout: connectionFactory.DefaultCommandTimeout);

        return rows.Select(row =>
        {
            var requestType = (RequestType)row.RequestType;
            return new RecordHistoryEntry
            {
                FormRequestId = row.FormRequestId,
                BulkFormRequestId = row.BulkFormRequestId,
                BulkRowNumber = row.BulkRowNumber,
                FormDefinitionId = row.FormDefinitionId,
                FormName = row.FormName,
                RequestType = requestType,
                Status = (RequestStatus)row.Status,
                RequestedByName = row.RequestedByName,
                RequestedAt = row.RequestedAt,
                ApprovedAt = row.ApprovedAt,
                Changes = BuildChanges(requestType, ParseValues(row.FieldValuesJson), ParseValues(row.OriginalValuesJson))
            };
        }).ToList();
    }

    /// <summary>Inserts list the submitted values, updates list only changed fields, deletes list nothing.</summary>
    public static List<RecordFieldChange> BuildChanges(RequestType requestType,
        IReadOnlyDictionary<string, object?> fieldValues, IReadOnlyDictionary<string, object?> originalValues)
    {
        return requestType switch
        {
            RequestType.Insert => fieldValues
                .Where(pair => !string.IsNullOrEmpty(AsText(pair.Value)))
                .Select(pair => new RecordFieldChange(pair.Key, null, pair.Value))
                .ToList(),
            RequestType.Update => fieldValues
                .Select(pair => new RecordFieldChange(pair.Key, FindOriginal(originalValues, pair.Key), pair.Value))
                .Where(change => !ValuesEqual(change.OldValue, change.NewValue))
                .ToList(),
            _ => new List<RecordFieldChange>()
        };
    }

    private static object? FindOriginal(IReadOnlyDictionary<string, object?> originalValues, string fieldName) =>
        originalValues.TryGetValue(fieldName, out var exact)
            ? exact
            : originalValues.FirstOrDefault(pair => string.Equals(pair.Key, fieldName, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool ValuesEqual(object? left, object? right)
    {
        var a = AsText(left);
        var b = AsText(right);
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        if (decimal.TryParse(a, NumberStyles.Number, CultureInfo.InvariantCulture, out var numberA)
            && decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out var numberB))
            return numberA == numberB;
        if (bool.TryParse(a, out var flagA) && bool.TryParse(b, out var flagB))
            return flagA == flagB;
        // Dates round-trip through JSON with varying precision/format (e.g. "2024-01-01" vs "2024-01-01T00:00:00").
        return DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateA)
            && DateTime.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateB)
            && dateA == dateB;
    }

    private static string AsText(object? value) => value switch
    {
        null => string.Empty,
        JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => string.Empty,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()?.Trim() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture).Trim(),
        _ => value.ToString()?.Trim() ?? string.Empty
    };

    private static Dictionary<string, object?> ParseValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private sealed class HistoryRow
    {
        public int? FormRequestId { get; set; }
        public int? BulkFormRequestId { get; set; }
        public int? BulkRowNumber { get; set; }
        public int FormDefinitionId { get; set; }
        public string FormName { get; set; } = string.Empty;
        public int RequestType { get; set; }
        public int Status { get; set; }
        public string RequestedByName { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? FieldValuesJson { get; set; }
        public string? OriginalValuesJson { get; set; }
    }
}
