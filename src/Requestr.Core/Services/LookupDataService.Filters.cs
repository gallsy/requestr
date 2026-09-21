using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Validation;

namespace Requestr.Core.Services;

public partial class LookupDataService
{
    public async Task<LookupFilterPage> SearchFilterLevelAsync(FormDefinition form, FormField field, int level,
        IReadOnlyList<string> filters, string? parentValue, string? search, int offset = 0, CancellationToken cancellationToken = default)
    {
        LookupConfigurationValidator.Validate(field);
        if (level < 0 || level >= field.LookupFilterLevels.Count || filters.Count != level || offset < 0)
            throw new ValidationException("Invalid lookup filter level or page.");
        if (!string.IsNullOrEmpty(field.LookupParentField) && string.IsNullOrEmpty(parentValue)) return new(Array.Empty<LookupOption>(), false);
        using var connection = CreateConnection(LookupConnectionName(form, field));
        await connection.OpenAsync(cancellationToken);
        var key = await GetKeyAsync(connection, form, field, cancellationToken);
        var parameters = new DynamicParameters(new { Pattern = FilterPattern(search), Offset = offset });
        var predicates = FilterPredicates(field, key, filters, parentValue, parameters);
        var column = Quote(key.Levels[level].Name);
        var sql = $"""
            SELECT DISTINCT {column} AS [Key]
            FROM {Quote(field.LookupSchema!)}.{Quote(field.LookupTable!)}
            WHERE {predicates} AND (@Pattern = N'' OR CONVERT(nvarchar(4000), {column}) LIKE @Pattern + N'%' ESCAPE N'~')
            ORDER BY {column} OFFSET @Offset ROWS FETCH NEXT 51 ROWS ONLY
            """;
        var rows = (await connection.QueryAsync<FilterRow>(new CommandDefinition(sql, parameters, commandTimeout: 10, cancellationToken: cancellationToken))).ToList();
        return new(rows.Take(50).Select(row => new LookupOption(FilterToken(row.Key), FilterText(row.Key))).ToList(), rows.Count > 50);
    }

    public async Task<IReadOnlyList<LookupOption>> SearchFilteredAsync(FormDefinition form, FormField field, string? search,
        string? parentValue, IReadOnlyList<string> filters, CancellationToken cancellationToken = default)
    {
        LookupConfigurationValidator.Validate(field);
        if (filters.Count != field.LookupFilterLevels.Count) throw new ValidationException("Select every lookup filter level.");
        if (!string.IsNullOrEmpty(field.LookupParentField) && string.IsNullOrEmpty(parentValue)) return Array.Empty<LookupOption>();
        using var connection = CreateConnection(LookupConnectionName(form, field));
        await connection.OpenAsync(cancellationToken);
        var key = await GetKeyAsync(connection, form, field, cancellationToken);
        var parameters = new DynamicParameters(new { Pattern = FilterPattern(search) });
        var predicates = FilterPredicates(field, key, filters, parentValue, parameters);
        var sql = $"""
            SELECT TOP (50) {Quote(field.LookupKeyColumn!)} AS [Key], CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) AS Label
            FROM {Quote(field.LookupSchema!)}.{Quote(field.LookupTable!)}
            WHERE {predicates} AND (@Pattern = N'' OR CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) LIKE @Pattern + N'%' ESCAPE N'~'
                OR CONVERT(nvarchar(4000), {Quote(field.LookupKeyColumn!)}) LIKE @Pattern + N'%' ESCAPE N'~')
            ORDER BY {Quote(field.LookupLabelColumn!)}, {Quote(field.LookupKeyColumn!)}
            """;
        return (await connection.QueryAsync<LookupRow>(new CommandDefinition(sql, parameters, commandTimeout: 10, cancellationToken: cancellationToken))).Select(ToOption).ToList();
    }

    public async Task<LookupOption?> ResolveFilteredAsync(FormDefinition form, FormField field, string value,
        string? parentValue, IReadOnlyList<string> filters, CancellationToken cancellationToken = default)
    {
        LookupConfigurationValidator.Validate(field);
        if (filters.Count != field.LookupFilterLevels.Count) throw new ValidationException("Select every lookup filter level.");
        if (!string.IsNullOrEmpty(field.LookupParentField) && string.IsNullOrEmpty(parentValue)) return null;
        using var connection = CreateConnection(LookupConnectionName(form, field));
        await connection.OpenAsync(cancellationToken);
        var key = await GetKeyAsync(connection, form, field, cancellationToken);
        var parameters = new DynamicParameters();
        string predicates;
        try
        {
            parameters.Add("Value", ParseKey(value, key));
            predicates = FilterPredicates(field, key, filters, parentValue, parameters);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ValidationException) { return null; }
        var sql = $"""
            SELECT {Quote(field.LookupKeyColumn!)} AS [Key], CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) AS Label
            FROM {Quote(field.LookupSchema!)}.{Quote(field.LookupTable!)}
            WHERE {Quote(field.LookupKeyColumn!)} = @Value AND {predicates}
            """;
        var row = await connection.QuerySingleOrDefaultAsync<LookupRow>(new CommandDefinition(sql, parameters, commandTimeout: 10, cancellationToken: cancellationToken));
        return row == null ? null : ToOption(row);
    }

    public async Task<LookupSelectionPath?> ResolvePathAsync(FormDefinition form, FormField field, string value,
        string? parentValue, bool enforceParent = true, CancellationToken cancellationToken = default)
    {
        LookupConfigurationValidator.Validate(field);
        if (enforceParent && !string.IsNullOrEmpty(field.LookupParentField) && string.IsNullOrEmpty(parentValue)) return null;
        using var connection = CreateConnection(LookupConnectionName(form, field));
        await connection.OpenAsync(cancellationToken);
        var key = await GetKeyAsync(connection, form, field, cancellationToken);
        var parameters = new DynamicParameters();
        try
        {
            parameters.Add("Value", ParseKey(value, key));
            if (enforceParent && key.Filter != null) parameters.Add("Parent", ParseKey(parentValue!, key.Filter));
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ValidationException) { return null; }
        var filter = enforceParent && key.Filter != null ? $"AND {Quote(field.LookupFilterColumn!)} = @Parent" : "";
        var projection = string.Concat(key.Levels.Select((column, index) => $", {Quote(column.Name)} AS [Filter{index}]"));
        var sql = $"""
            SELECT {Quote(field.LookupKeyColumn!)} AS [Key], CONVERT(nvarchar(4000), {Quote(field.LookupLabelColumn!)}) AS Label {projection}
            FROM {Quote(field.LookupSchema!)}.{Quote(field.LookupTable!)} WHERE {Quote(field.LookupKeyColumn!)} = @Value {filter}
            """;
        var row = (IDictionary<string, object>?)await connection.QuerySingleOrDefaultAsync(new CommandDefinition(sql, parameters, commandTimeout: 10, cancellationToken: cancellationToken));
        if (row == null) return null;
        return new(ToOption(new LookupRow { Key = row["Key"], Label = row["Label"] as string }),
            key.Levels.Select((column, index) => FilterToken(row[$"Filter{index}"])).ToList());
    }

    private static string FilterPredicates(FormField field, LookupColumn key, IReadOnlyList<string> filters, string? parentValue, DynamicParameters parameters)
    {
        var predicates = new List<string> { "1 = 1" };
        if (key.Filter != null)
        {
            parameters.Add("Parent", ParseKey(parentValue!, key.Filter));
            predicates.Add($"{Quote(field.LookupFilterColumn!)} = @Parent");
        }
        for (var index = 0; index < filters.Count; index++)
        {
            string? value;
            try { value = JsonSerializer.Deserialize<string?>(filters[index]); }
            catch (JsonException) { throw new ValidationException("Invalid lookup filter value."); }
            var column = key.Levels[index];
            if (value == null)
            {
                if (!column.IsNullable) throw new ValidationException("This filter does not allow an empty value.");
                predicates.Add($"{Quote(column.Name)} IS NULL");
            }
            else
            {
                parameters.Add($"Filter{index}", ParseKey(value, column));
                predicates.Add($"{Quote(column.Name)} = @Filter{index}");
            }
        }
        return string.Join(" AND ", predicates);
    }

    private static string FilterToken(object? value) => JsonSerializer.Serialize(value == null || value is DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture));
    private static string FilterText(object? value) => value == null || value is DBNull ? "(blank)" : Convert.ToString(value, CultureInfo.InvariantCulture) is { Length: > 0 } text ? text : "(empty)";
    private static string FilterPattern(string? search)
    {
        var pattern = (search ?? "").Trim();
        if (pattern.Length > 200) pattern = pattern[..200];
        return pattern.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[");
    }

    private sealed class FilterRow
    {
        public object? Key { get; set; }
    }
}