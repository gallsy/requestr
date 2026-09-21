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
        FormConditions.ValidateConfiguration(form);
        if (HasConditions(form))
        {
            using var destination = CreateConnection(form.DatabaseConnectionName);
            var columns = (await destination.QueryAsync<DestinationColumn>(new CommandDefinition("""
                SELECT c.name AS Name, c.is_nullable AS IsNullable, c.default_object_id AS DefaultId,
                    c.is_identity AS IsIdentity, c.is_computed AS IsComputed
                FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = @Schema AND t.name = @TableName
                """, new { form.Schema, form.TableName }, cancellationToken: cancellationToken))).ToList();
            foreach (var field in form.Fields.Where(field => !string.IsNullOrWhiteSpace(field.VisibilityCondition) ||
                !string.IsNullOrWhiteSpace(form.Sections.FirstOrDefault(section => section.Id == field.FormSectionId)?.VisibilityCondition)))
            {
                var column = columns.SingleOrDefault(column => column.Name == field.Name);
                if (column == null || column.IsIdentity || column.IsComputed || field.ComputedValueType is not (null or ComputedValueType.None) ||
                    (!column.IsNullable && column.DefaultId == 0))
                    throw new ValidationException($"{field.DisplayName}: conditional visibility requires a nullable column or a database default, and cannot target generated columns.");
            }
        }
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
        => await SearchDependentAsync(form, field, search, null, cancellationToken);

    public async Task<IReadOnlyList<LookupOption>> SearchDependentAsync(FormDefinition form, FormField field, string? search, string? parentValue, CancellationToken cancellationToken = default)
    {
        LookupConfigurationValidator.Validate(field);
        if (!string.IsNullOrEmpty(field.LookupParentField) && string.IsNullOrEmpty(parentValue)) return Array.Empty<LookupOption>();
        using var connection = CreateConnection(LookupConnectionName(form, field));
        await connection.OpenAsync(cancellationToken);
        var key = await GetKeyAsync(connection, form, field, cancellationToken);
        var parent = key.Filter == null ? null : ParseKey(parentValue!, key.Filter);
        var filter = key.Filter == null ? "" : $"AND {Quote(field.LookupFilterColumn!)} = @Parent";
        var pattern = (search ?? "").Trim();
        if (pattern.Length > 200) pattern = pattern[..200];
        pattern = pattern.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
        var sql = $"""
            SELECT TOP (50) {Quote(field.LookupKeyColumn!)} AS [Key],
                CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) AS Label
            FROM {Quote(field.LookupSchema!)}.{Quote(field.LookupTable!)}
            WHERE (@Pattern = N'' OR CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) LIKE @Pattern + N'%' ESCAPE N'~'
                OR CONVERT(nvarchar(4000), {Quote(field.LookupKeyColumn!)}) LIKE @Pattern + N'%' ESCAPE N'~')
            {filter}
            ORDER BY {Quote(field.LookupLabelColumn!)}, {Quote(field.LookupKeyColumn!)}
            """;
        var rows = await connection.QueryAsync<LookupRow>(new CommandDefinition(sql, new { Pattern = pattern, Parent = parent }, commandTimeout: 10, cancellationToken: cancellationToken));
        return rows.Select(ToOption).ToList();
    }

    public async Task<LookupOption?> ResolveAsync(FormDefinition form, FormField field, string value, CancellationToken cancellationToken = default)
        => await ResolveCoreAsync(form, field, value, null, false, cancellationToken);

    public Task<LookupOption?> ResolveDependentAsync(FormDefinition form, FormField field, string value, string? parentValue, CancellationToken cancellationToken = default)
        => ResolveCoreAsync(form, field, value, parentValue, true, cancellationToken);

    private async Task<LookupOption?> ResolveCoreAsync(FormDefinition form, FormField field, string value, string? parentValue, bool enforceParent, CancellationToken cancellationToken)
    {
        LookupConfigurationValidator.Validate(field);
        if (enforceParent && !string.IsNullOrEmpty(field.LookupParentField) && string.IsNullOrEmpty(parentValue)) return null;
        using var connection = CreateConnection(LookupConnectionName(form, field));
        await connection.OpenAsync(cancellationToken);
        var key = await GetKeyAsync(connection, form, field, cancellationToken);
        object typedValue;
        object? parent = null;
        try
        {
            typedValue = ParseKey(value, key);
            if (enforceParent && key.Filter != null) parent = ParseKey(parentValue!, key.Filter);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ValidationException)
        { return null; }
        var filter = enforceParent && key.Filter != null ? $"AND {Quote(field.LookupFilterColumn!)} = @Parent" : "";
        var sql = $"""
            SELECT {Quote(field.LookupKeyColumn!)} AS [Key], CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) AS Label
            FROM {Quote(field.LookupSchema!)}.{Quote(field.LookupTable!)} WHERE {Quote(field.LookupKeyColumn!)} = @Value
            {filter}
            """;
        var row = await connection.QuerySingleOrDefaultAsync<LookupRow>(new CommandDefinition(sql, new { Value = typedValue, Parent = parent }, commandTimeout: 10, cancellationToken: cancellationToken));
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
                if (field.IsRequired && FormConditions.IsApplicable(form, field, values)) throw new ValidationException($"{field.DisplayName} is required.");
                values[valueName] = null;
                continue;
            }
            var option = await ResolveDependentAsync(form, field, text,
                string.IsNullOrEmpty(field.LookupParentField) ? null : FormConditions.Text(FormConditions.Value(values, field.LookupParentField)), cancellationToken)
                ?? throw new ValidationException($"{field.DisplayName}: the selected lookup key is unavailable. Choose an available value.");
            values[valueName] = SqlTypeConverter.ConvertToSqlType(option.Value, field.SqlDataType ?? field.DataType);
        }
    }

    public async Task ValidateSubmissionAsync(FormDefinition form, Dictionary<string, object?> values, RequestType requestType, IReadOnlyDictionary<string, object?>? originalValues = null, CancellationToken cancellationToken = default)
    {
        if (requestType == RequestType.Delete) return;
        if (!HasConditions(form)) { await ValidateValuesAsync(form, values, cancellationToken); return; }
        FormConditions.ValidateConfiguration(form);
        var effective = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in values.Keys) FormConditions.Value(values, name);
        if (requestType == RequestType.Update)
        {
            using var connection = CreateConnection(form.DatabaseConnectionName);
            var keys = (await connection.QueryAsync<string>(new CommandDefinition("""
                SELECT c.name FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
                WHERE i.is_primary_key=1 AND s.name=@Schema AND t.name=@TableName ORDER BY ic.key_ordinal
                """, new { form.Schema, form.TableName }, cancellationToken: cancellationToken))).ToList();
            if (keys.Count == 0 || originalValues == null) throw new ValidationException("Conditional updates require the original record key.");
            var parameters = new DynamicParameters();
            var predicates = keys.Select((name, index) =>
            {
                var value = FormConditions.Value(originalValues, name) ?? throw new ValidationException("The original record key is missing.");
                parameters.Add($"Key{index}", value);
                return $"{Quote(name)} = @Key{index}";
            });
            var record = await connection.QuerySingleOrDefaultAsync(new CommandDefinition(
                $"SELECT * FROM {Quote(form.Schema)}.{Quote(form.TableName)} WHERE {string.Join(" AND ", predicates)}", parameters, cancellationToken: cancellationToken));
            if (record == null) throw new ValidationException("The record being updated no longer exists.");
            foreach (var pair in (IDictionary<string, object>)record) effective[pair.Key] = pair.Value;
        }
        var baseline = new Dictionary<string, object?>(effective, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values) effective[pair.Key] = SqlTypeConverter.UnwrapJsonElement(pair.Value);
        var hidden = form.Fields.Where(field => !FormConditions.IsApplicable(form, field, effective)).ToList();
        foreach (var field in hidden)
        {
            foreach (var name in values.Keys.Where(name => string.Equals(name, field.Name, StringComparison.OrdinalIgnoreCase)).ToList()) values.Remove(name);
            if (baseline.TryGetValue(field.Name, out var original)) effective[field.Name] = original;
            else effective.Remove(field.Name);
        }
        foreach (var field in form.Fields.Where(field => field.IsVisible && !field.IsReadOnly && field.ComputedValueType is null or ComputedValueType.None))
        {
            if (!FormConditions.IsApplicable(form, field, effective)) continue;
            var validation = InputValidator.ValidateInput(FormConditions.Text(FormConditions.Value(effective, field.Name)), field);
            if (!validation.IsValid) throw new ValidationException(string.Join("; ", validation.Errors));
        }
        await ValidateValuesAsync(form, effective, cancellationToken);
        foreach (var name in values.Keys.ToList())
            if (effective.TryGetValue(name, out var value)) values[name] = value;
    }

    private static bool HasConditions(FormDefinition form) => form.Fields.Any(field => !string.IsNullOrWhiteSpace(field.VisibilityCondition) || !string.IsNullOrWhiteSpace(field.LookupParentField)) || form.Sections.Any(section => !string.IsNullOrWhiteSpace(section.VisibilityCondition));

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
        var destinationColumns = (await (destinationConnection ?? connection).QueryAsync<LookupColumn>(new CommandDefinition(sql,
            new { Schema = form.Schema, Table = form.TableName }, commandTimeout: 10, cancellationToken: cancellationToken)))
            .ToList();
        var destination = destinationColumns.SingleOrDefault(column => column.Name == field.Name);
        if (destination == null || !Compatible(key, destination))
            throw new ValidationException($"{field.DisplayName}: the lookup key type or length is incompatible with the destination column.");
        if (!string.IsNullOrEmpty(field.LookupParentField))
        {
            var parent = destinationColumns.SingleOrDefault(column => column.Name == field.LookupParentField);
            key.Filter = columns.SingleOrDefault(column => column.Name == field.LookupFilterColumn);
            if (parent == null || key.Filter == null || !SupportedKey(parent.DataType) || !Compatible(parent, key.Filter))
                throw new ValidationException($"{field.DisplayName}: the parent field and lookup filter column have incompatible types or lengths.");
        }
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
        public LookupColumn? Filter { get; set; }
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public short MaxLength { get; set; }
        public bool IsNullable { get; set; }
        public bool IsUnique { get; set; }
    }

    private sealed class DestinationColumn
    {
        public string Name { get; set; } = "";
        public bool IsNullable { get; set; }
        public int DefaultId { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
    }
}