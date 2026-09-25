using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Requestr.Core.Models;
using Requestr.Core.Validation;

namespace Requestr.Core.Services;

public partial class DataViewService
{
    private sealed record LookupQuery(FormField Field, string Join, string DisplayExpression, string ResultColumn);

    private static string QuoteIdentifier(string name) => $"[{name.Replace("]", "]]")}]";

    private async Task<List<LookupQuery>> BuildLookupQueriesAsync(SqlConnection destination, FormDefinition form, List<FormField> visibleFields)
    {
        var queries = new List<LookupQuery>();
        foreach (var field in visibleFields.Where(field => field.OptionSource == FieldOptionSource.DatabaseLookup))
        {
            LookupConfigurationValidator.Validate(field);
            var connectionName = string.IsNullOrWhiteSpace(field.LookupDatabaseConnectionName)
                ? form.DatabaseConnectionName : field.LookupDatabaseConnectionName;
            var connectionString = _configuration[$"DatabaseConnections:{connectionName}"] ?? _configuration.GetConnectionString(connectionName)
                ?? throw new InvalidOperationException("The lookup database connection is unavailable.");
            var sourceTable = $"{QuoteIdentifier(field.LookupSchema!)}.{QuoteIdentifier(field.LookupTable!)}";
            var keyColumn = QuoteIdentifier(field.LookupKeyColumn!);
            var labelColumn = QuoteIdentifier(field.LookupLabelColumn!);
            var alias = QuoteIdentifier($"lookup{queries.Count}");
            var rawValue = $"[data].{QuoteIdentifier(field.Name)}";

            if (!string.Equals(new SqlConnectionStringBuilder(connectionString).ConnectionString,
                new SqlConnectionStringBuilder(GetConnectionString(form.DatabaseConnectionName)).ConnectionString, StringComparison.Ordinal))
            {
                var temporaryTable = QuoteIdentifier($"#DataViewLookup{queries.Count}");
                await destination.ExecuteAsync($"""
                    SELECT TOP (0) CASE WHEN 1 = 1 THEN {QuoteIdentifier(field.Name)} END AS [LookupKey],
                        CAST(NULL AS nvarchar(max)) AS [LookupLabel]
                    INTO {temporaryTable} FROM {QuoteIdentifier(form.Schema)}.{QuoteIdentifier(form.TableName)};
                    """);
                using var source = new SqlConnection(connectionString);
                await source.OpenAsync();
                using var command = source.CreateCommand();
                command.CommandText = $"SELECT {keyColumn}, CONVERT(nvarchar(max), {labelColumn}) FROM {sourceTable}";
                using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
                using var copy = new SqlBulkCopy(destination) { DestinationTableName = temporaryTable, EnableStreaming = true, BatchSize = 1000 };
                copy.ColumnMappings.Add(0, "LookupKey");
                copy.ColumnMappings.Add(1, "LookupLabel");
                await copy.WriteToServerAsync(reader);
                sourceTable = temporaryTable;
                keyColumn = "[LookupKey]";
                labelColumn = "[LookupLabel]";
            }

            var label = $"CONVERT(nvarchar(max), {alias}.{labelColumn})";
            var display = $"CASE WHEN NULLIF(LTRIM(RTRIM({label})), N'') IS NULL THEN CONVERT(nvarchar(max), {rawValue}) ELSE {label} END";
            queries.Add(new(field, $"LEFT JOIN {sourceTable} AS {alias} ON {alias}.{keyColumn} = {rawValue}", display,
                $"__lookup_{Guid.NewGuid():N}"));
        }
        return queries;
    }
}