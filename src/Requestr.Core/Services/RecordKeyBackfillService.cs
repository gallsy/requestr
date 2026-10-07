using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using Requestr.Core.Utilities;

namespace Requestr.Core.Services;

/// <summary>
/// Fills RecordKey on requests and bulk items created before record keys existed. Primary keys live in the
/// target databases, so this runs from the app rather than a SQL migration. Safe to rerun: only NULL keys are touched.
/// </summary>
public sealed class RecordKeyBackfillService(
    IDbConnectionFactory connectionFactory,
    IDataService dataService,
    ILogger<RecordKeyBackfillService> logger)
{
    private const int BatchSize = 500;

    // Bulk workflow placeholder requests (BulkFormRequestId set) don't target a record; their items are keyed instead.
    private const string RequestCandidatesSql = """
        SELECT TOP (@BatchSize) fr.Id, fr.RequestType, fr.FieldValues, fr.OriginalValues, fr.AppliedRecordKey AS LegacyKey,
               fd.DatabaseConnectionName, fd.[Schema] AS TargetSchema, fd.TableName
        FROM FormRequests fr
        INNER JOIN FormDefinitions fd ON fd.Id = fr.FormDefinitionId
        WHERE fr.RecordKey IS NULL AND fr.BulkFormRequestId IS NULL AND fr.Id > @AfterId
        ORDER BY fr.Id
        """;

    private const string ItemCandidatesSql = """
        SELECT TOP (@BatchSize) i.Id, b.RequestType, i.FieldValues, i.OriginalValues, CAST(NULL AS nvarchar(255)) AS LegacyKey,
               fd.DatabaseConnectionName, fd.[Schema] AS TargetSchema, fd.TableName
        FROM BulkFormRequestItems i
        INNER JOIN BulkFormRequests b ON b.Id = i.BulkFormRequestId
        INNER JOIN FormDefinitions fd ON fd.Id = b.FormDefinitionId
        WHERE i.RecordKey IS NULL AND i.Id > @AfterId
        ORDER BY i.Id
        """;

    private readonly Dictionary<(string Connection, string Schema, string Table), List<string>?> _primaryKeys = new();

    public async Task<RecordKeyBackfillResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var requests = await BackfillAsync(RequestCandidatesSql,
            "UPDATE FormRequests SET RecordKey = @RecordKey WHERE Id = @Id AND RecordKey IS NULL", cancellationToken);
        var items = await BackfillAsync(ItemCandidatesSql,
            "UPDATE BulkFormRequestItems SET RecordKey = @RecordKey WHERE Id = @Id AND RecordKey IS NULL", cancellationToken);
        var result = new RecordKeyBackfillResult(requests.Updated, requests.Unresolved, items.Updated, items.Unresolved);

        if (result.RequestsUpdated + result.ItemsUpdated > 0)
            logger.LogInformation("Record key backfill updated {Requests} requests and {Items} bulk items", result.RequestsUpdated, result.ItemsUpdated);
        if (result.RequestsUnresolved + result.ItemsUnresolved > 0)
            logger.LogInformation("Record key backfill could not key {Requests} requests and {Items} bulk items (e.g. unapplied identity inserts)",
                result.RequestsUnresolved, result.ItemsUnresolved);
        return result;
    }

    private async Task<(int Updated, int Unresolved)> BackfillAsync(string candidatesSql, string updateSql, CancellationToken cancellationToken)
    {
        int updated = 0, unresolved = 0, afterId = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Candidate> batch;
            using (var connection = await connectionFactory.CreateConnectionAsync())
            {
                batch = (await connection.QueryAsync<Candidate>(new CommandDefinition(candidatesSql,
                    new { BatchSize, AfterId = afterId }, cancellationToken: cancellationToken))).ToList();
            }
            if (batch.Count == 0)
                return (updated, unresolved);
            afterId = batch[^1].Id;

            var keys = new List<object>();
            foreach (var candidate in batch)
            {
                var key = await BuildKeyAsync(candidate);
                if (key == null)
                    unresolved++;
                else
                    keys.Add(new { candidate.Id, RecordKey = key });
            }

            if (keys.Count > 0)
            {
                using var connection = await connectionFactory.CreateConnectionAsync();
                updated += await connection.ExecuteAsync(new CommandDefinition(updateSql, keys, cancellationToken: cancellationToken));
            }
        }
    }

    private async Task<string?> BuildKeyAsync(Candidate candidate)
    {
        var primaryKeyColumns = await GetPrimaryKeyColumnsAsync(candidate);
        if (primaryKeyColumns == null)
            return null;

        var requestType = (RequestType)candidate.RequestType;
        return RecordKeyBuilder.ForRequest(primaryKeyColumns, requestType, ParseValues(candidate.FieldValues), ParseValues(candidate.OriginalValues))
            ?? (requestType == RequestType.Insert ? RecordKeyBuilder.FromLegacyAppliedKey(candidate.LegacyKey, primaryKeyColumns) : null);
    }

    private async Task<List<string>?> GetPrimaryKeyColumnsAsync(Candidate candidate)
    {
        var table = (candidate.DatabaseConnectionName, candidate.TargetSchema, candidate.TableName);
        if (_primaryKeys.TryGetValue(table, out var cached))
            return cached;
        try
        {
            cached = await dataService.GetPrimaryKeyColumnsAsync(candidate.DatabaseConnectionName, candidate.TableName, candidate.TargetSchema);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Record key backfill skipped {Schema}.{Table} on {Connection}",
                candidate.TargetSchema, candidate.TableName, candidate.DatabaseConnectionName);
            cached = null;
        }
        return _primaryKeys[table] = cached;
    }

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

    private sealed class Candidate
    {
        public int Id { get; set; }
        public int RequestType { get; set; }
        public string? FieldValues { get; set; }
        public string? OriginalValues { get; set; }
        public string? LegacyKey { get; set; }
        public string DatabaseConnectionName { get; set; } = string.Empty;
        public string TargetSchema { get; set; } = "dbo";
        public string TableName { get; set; } = string.Empty;
    }
}

public sealed record RecordKeyBackfillResult(int RequestsUpdated, int RequestsUnresolved, int ItemsUpdated, int ItemsUnresolved);
