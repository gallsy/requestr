using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Utilities;
using Requestr.Core.Validation;

namespace Requestr.Core.Services;

public class LookupDataService(IConfiguration configuration) : ILookupDataService
{
    public async Task ValidateConfigurationAsync(FormDefinition form, CancellationToken cancellationToken = default)
    {
        foreach (var field in form.Fields.DistinctBy(field => field.Name))
        {
            LookupConfigurationValidator.Validate(field);
            if (field.OptionSource != FieldOptionSource.DatabaseLookup) continue;
            using var connection = CreateConnection(LookupConnectionName(form, field));
            await connection.OpenAsync(cancellationToken);
            await GetKeyAsync(connection, form, field, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<LookupOption>> SearchAsync(FormDefinition form, FormField field, string? search, CancellationToken cancellationToken = default)
    {
        LookupConfigurationValidator.Validate(field);
        using var connection = CreateConnection(LookupConnectionName(form, field));
        await connection.OpenAsync(cancellationToken);
        await GetKeyAsync(connection, form, field, cancellationToken);
        var pattern = (search ?? "").Trim();
        if (pattern.Length > 200) pattern = pattern[..200];
        pattern = pattern.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
        var sql = $"""
            SELECT TOP (50) {Quote(field.LookupKeyColumn!)} AS [Key],
                CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) AS Label
            FROM {Quote(field.LookupSchema!)}.{Quote(field.LookupTable!)}
            WHERE (@Pattern = N'' OR CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) LIKE @Pattern + N'%' ESCAPE N'~'
                OR CONVERT(nvarchar(4000), {Quote(field.LookupKeyColumn!)}) LIKE @Pattern + N'%' ESCAPE N'~')
            ORDER BY {Quote(field.LookupLabelColumn!)}, {Quote(field.LookupKeyColumn!)}
            """;
        var rows = await connection.QueryAsync<LookupRow>(new CommandDefinition(sql, new { Pattern = pattern }, commandTimeout: 10, cancellationToken: cancellationToken));
        return rows.Select(ToOption).ToList();
    }

    public async Task<LookupOption?> ResolveAsync(FormDefinition form, FormField field, string value, CancellationToken cancellationToken = default)
    {
        LookupConfigurationValidator.Validate(field);
        using var connection = CreateConnection(LookupConnectionName(form, field));
        await connection.OpenAsync(cancellationToken);
        var key = await GetKeyAsync(connection, form, field, cancellationToken);
        object typedValue;
        try { typedValue = ParseKey(value, key); }
        catch (Exception exception) when (exception is FormatException or OverflowException or ValidationException)
        { return null; }
        var sql = $"""
            SELECT {Quote(field.LookupKeyColumn!)} AS [Key], CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) AS Label
            FROM {Quote(field.LookupSchema!)}.{Quote(field.LookupTable!)} WHERE {Quote(field.LookupKeyColumn!)} = @Value
            """;
        var row = await connection.QuerySingleOrDefaultAsync<LookupRow>(new CommandDefinition(sql, new { Value = typedValue }, commandTimeout: 10, cancellationToken: cancellationToken));
        return row == null ? null : ToOption(row);
    }

    public async Task ValidateValuesAsync(FormDefinition form, Dictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        foreach (var field in form.Fields.Where(field => field.OptionSource == FieldOptionSource.DatabaseLookup).DistinctBy(field => field.Name))
        {
            var names = values.Keys.Where(name => string.Equals(name, field.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (names.Count == 0) continue;
            if (names.Count > 1) throw new ValidationException($"{field.DisplayName}: duplicate lookup field values.");
            var valueName = names[0];
            var value = values[valueName];
            var text = Convert.ToString(SqlTypeConverter.UnwrapJsonElement(value), CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(text))
            {
                if (field.IsRequired) throw new ValidationException($"{field.DisplayName} is required.");
                values[valueName] = null;
                continue;
            }
            var option = await ResolveAsync(form, field, text, cancellationToken)
                ?? throw new ValidationException($"{field.DisplayName}: the selected lookup key is unavailable. Choose an available value.");
            values[valueName] = SqlTypeConverter.ConvertToSqlType(option.Value, field.SqlDataType ?? field.DataType);
        }
    }

    private static string LookupConnectionName(FormDefinition form, FormField field) =>
        string.IsNullOrWhiteSpace(field.LookupDatabaseConnectionName) ? form.DatabaseConnectionName : field.LookupDatabaseConnectionName;

    private SqlConnection CreateConnection(string connectionName) => new(
        configuration[$"DatabaseConnections:{connectionName}"]
        ?? configuration.GetConnectionString(connectionName)
        ?? throw new ValidationException("The lookup database connection is unavailable."));

    private async Task<LookupColumn> GetKeyAsync(SqlConnection connection, FormDefinition form, FormField field, CancellationToken cancellationToken)
    {
        if (field.OptionSource != FieldOptionSource.DatabaseLookup)
            throw new ValidationException("This field is not a database lookup.");
        const string sql = """
            SELECT columnInfo.name AS Name, TYPE_NAME(columnInfo.system_type_id) AS DataType,
                   columnInfo.max_length AS MaxLength, columnInfo.is_nullable AS IsNullable,
                   CONVERT(bit, CASE WHEN EXISTS (
                       SELECT 1 FROM sys.indexes indexInfo
                       JOIN sys.index_columns indexColumn ON indexColumn.object_id = indexInfo.object_id AND indexColumn.index_id = indexInfo.index_id
                       WHERE indexInfo.object_id = tableInfo.object_id AND indexInfo.is_unique = 1
                         AND indexInfo.has_filter = 0 AND indexInfo.is_disabled = 0 AND indexInfo.is_hypothetical = 0
                         AND indexColumn.column_id = columnInfo.column_id AND indexColumn.key_ordinal = 1
                         AND NOT EXISTS (SELECT 1 FROM sys.index_columns otherColumn WHERE otherColumn.object_id = indexInfo.object_id
                             AND otherColumn.index_id = indexInfo.index_id AND otherColumn.key_ordinal > 1)
                   ) THEN 1 ELSE 0 END) AS IsUnique
            FROM sys.tables tableInfo
            JOIN sys.schemas schemaInfo ON schemaInfo.schema_id = tableInfo.schema_id
            JOIN sys.columns columnInfo ON columnInfo.object_id = tableInfo.object_id
            WHERE schemaInfo.name = @Schema AND tableInfo.name = @Table
            """;
        var columns = (await connection.QueryAsync<LookupColumn>(new CommandDefinition(sql,
            new { Schema = field.LookupSchema, Table = field.LookupTable }, commandTimeout: 10, cancellationToken: cancellationToken))).ToList();
        var key = columns.SingleOrDefault(column => column.Name == field.LookupKeyColumn);
        var label = columns.SingleOrDefault(column => column.Name == field.LookupLabelColumn);
        if (key == null || !key.IsUnique || key.IsNullable || !SupportedKey(key.DataType))
            throw new ValidationException($"{field.DisplayName}: choose a non-null, single-column primary or unique key (integer, string, or GUID).");
        if (label == null || label.DataType is not ("varchar" or "nvarchar" or "char" or "nchar"))
            throw new ValidationException($"{field.DisplayName}: choose a text label column.");
        using var destinationConnection = string.Equals(LookupConnectionName(form, field), form.DatabaseConnectionName, StringComparison.OrdinalIgnoreCase)
            ? null : CreateConnection(form.DatabaseConnectionName);
        if (destinationConnection != null) await destinationConnection.OpenAsync(cancellationToken);
        var destination = (await (destinationConnection ?? connection).QueryAsync<LookupColumn>(new CommandDefinition(sql,
            new { Schema = form.Schema, Table = form.TableName }, commandTimeout: 10, cancellationToken: cancellationToken)))
            .SingleOrDefault(column => column.Name == field.Name);
        if (destination == null || !Compatible(key, destination))
            throw new ValidationException($"{field.DisplayName}: the lookup key type or length is incompatible with the destination column.");
        return key;
    }

    private static bool SupportedKey(string type) => type is "tinyint" or "smallint" or "int" or "bigint" or "uniqueidentifier" or "varchar" or "nvarchar" or "char" or "nchar";

    private static bool Compatible(LookupColumn source, LookupColumn destination)
    {
        var integers = new[] { "tinyint", "smallint", "int", "bigint" };
        var sourceRank = Array.IndexOf(integers, source.DataType);
        if (sourceRank >= 0) return Array.IndexOf(integers, destination.DataType) >= sourceRank;
        if (source.DataType == "uniqueidentifier") return destination.DataType == source.DataType;
        return source.DataType == destination.DataType && source.MaxLength > 0 &&
            (destination.MaxLength == -1 || destination.MaxLength >= source.MaxLength);
    }

    private static object ParseKey(string value, LookupColumn key) => key.DataType switch
    {
        "tinyint" => byte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
        "smallint" => short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
        "int" => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
        "bigint" => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
        "uniqueidentifier" => Guid.Parse(value),
        _ when value.Length > (key.DataType.StartsWith('n') ? key.MaxLength / 2 : key.MaxLength) => throw new ValidationException("Lookup key is too long."),
        _ => value
    };

    private static LookupOption ToOption(LookupRow row)
    {
        var value = Convert.ToString(row.Key, CultureInfo.InvariantCulture) ?? "";
        return new(value, string.IsNullOrWhiteSpace(row.Label) ? value : row.Label);
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private sealed class LookupRow
    {
        public object Key { get; set; } = "";
        public string? Label { get; set; }
    }

    private sealed class LookupColumn
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public short MaxLength { get; set; }
        public bool IsNullable { get; set; }
        public bool IsUnique { get; set; }
    }
}