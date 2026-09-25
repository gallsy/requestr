using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;

namespace Requestr.Core.Services;

public partial class DataViewService : IDataViewService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataViewService> _logger;
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly IDataService _dataService;
    private readonly IBulkFormRequestService _bulkFormRequestService;
    private readonly Dictionary<string, string> _connectionStrings;
    private readonly string _defaultConnectionString;

    public DataViewService(
        IConfiguration configuration,
        ILogger<DataViewService> logger,
        IFormDefinitionService formDefinitionService,
        IDataService dataService,
        IBulkFormRequestService bulkFormRequestService)
    {
        _configuration = configuration;
        _logger = logger;
        _formDefinitionService = formDefinitionService;
        _dataService = dataService;
        _bulkFormRequestService = bulkFormRequestService;
        
        // Load connection strings from configuration
        _connectionStrings = new Dictionary<string, string>();
        
        // Load predefined database connections from configuration (same approach as DataService)
        var dbSection = _configuration.GetSection("DatabaseConnections");
        foreach (var child in dbSection.GetChildren())
        {
            var name = child.Key;
            var connectionString = child.Value;
            if (!string.IsNullOrEmpty(connectionString))
            {
                _connectionStrings[name] = connectionString;
            }
        }
        
        // Also load the default connection
        _defaultConnectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
    }

    public async Task<DataViewResult> GetDataAsync(int formDefinitionId, int page = 1, int pageSize = 50, string? searchTerm = null, Dictionary<string, object?>? filters = null, string? sortColumn = null, string sortDirection = "ASC")
    {
        var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formDefinitionId);
        if (formDefinition == null)
        {
            throw new ArgumentException($"Form definition with ID {formDefinitionId} not found");
        }

        var result = new DataViewResult
        {
            CurrentPage = page,
            PageSize = pageSize
        };

        try
        {
            // Get all fields visible in data view to determine columns
            var visibleFields = formDefinition.Fields.Where(f => f.IsVisibleInDataView).OrderBy(f => f.DisplayOrder).ToList();
            result.Columns = visibleFields.Select(f => f.Name).ToList();

            // Get primary key columns
            result.PrimaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                formDefinition.DatabaseConnectionName,
                formDefinition.TableName,
                formDefinition.Schema);

            using var connection = new SqlConnection(GetConnectionString(formDefinition.DatabaseConnectionName));
            await connection.OpenAsync();
            var lookups = await BuildLookupQueriesAsync(connection, formDefinition, visibleFields);
            var fromClause = $"{QuoteIdentifier(formDefinition.Schema)}.{QuoteIdentifier(formDefinition.TableName)} AS [data] {string.Join(" ", lookups.Select(lookup => lookup.Join))}";

            // Build query with pagination and filtering
            var whereConditions = new List<string>();
            var parameters = new DynamicParameters();

            // Add search functionality — each word is an independent term (AND logic),
            // while quoted phrases are treated as a single term.
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var searchableFields = visibleFields
                    .SelectMany(field =>
                    {
                        var column = $"[data].{QuoteIdentifier(field.Name)}";
                        return (field.SqlDataType ?? field.DataType).ToLowerInvariant() switch
                        {
                            "string" or "text" or "nvarchar" or "varchar" or "char" or "nchar" or "ntext" => new[] { column },
                            "bool" or "boolean" or "bit" => new[]
                            {
                                $"CASE WHEN {column} = 1 THEN N'True' WHEN {column} = 0 THEN N'False' END",
                                $"CONVERT(nvarchar(1), {column})"
                            },
                            "number" or "integer" or "tinyint" or "smallint" or "int" or "bigint" or
                            "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" or "double" =>
                                new[] { $"CONVERT(nvarchar(100), {column})" },
                            _ => Array.Empty<string>()
                        };
                    })
                    .Concat(lookups.Select(lookup => lookup.DisplayExpression))
                    .ToList();

                if (searchableFields.Any())
                {
                    var terms = ParseSearchTerms(searchTerm);
                    for (int i = 0; i < terms.Count; i++)
                    {
                        var paramName = $"SearchTerm{i}";
                        var termConditions = searchableFields
                            .Select(expression => $"{expression} LIKE @{paramName}")
                            .ToList();
                        whereConditions.Add($"({string.Join(" OR ", termConditions)})");
                        parameters.Add(paramName, $"%{terms[i]}%");
                    }
                }
            }

            // Add filters
            if (filters != null && filters.Any())
            {
                foreach (var filter in filters)
                {
                    if (!formDefinition.Fields.Any(field => field.Name == filter.Key))
                        throw new ArgumentException("Unknown filter column.", nameof(filters));
                    var parameterName = $"Filter{parameters.ParameterNames.Count()}";
                    whereConditions.Add($"[data].{QuoteIdentifier(filter.Key)} = @{parameterName}");
                    parameters.Add(parameterName, filter.Value);
                }
            }

            var whereClause = whereConditions.Any() ? $"WHERE {string.Join(" AND ", whereConditions)}" : "";

            // Get total count
            var countSql = $@"
                SELECT COUNT(*)
                FROM {fromClause}
                {whereClause}";

            result.TotalCount = await connection.QuerySingleAsync<int>(countSql, parameters);
            result.TotalPages = (int)Math.Ceiling((double)result.TotalCount / pageSize);

            // Get paginated data
            var offset = (page - 1) * pageSize;
            var columnsList = string.Join(", ", result.Columns.Select(column => $"[data].{QuoteIdentifier(column)}")
                .Concat(lookups.Select(lookup => $"{lookup.DisplayExpression} AS {QuoteIdentifier(lookup.ResultColumn)}")));
            var defaultOrderByColumn = result.PrimaryKeyColumns.FirstOrDefault() ?? result.Columns.FirstOrDefault() ?? "1";

            // Validate sort column against available columns to prevent SQL injection
            var orderByColumn = defaultOrderByColumn;
            if (!string.IsNullOrEmpty(sortColumn) && result.Columns.Contains(sortColumn))
            {
                orderByColumn = sortColumn;
            }
            var direction = string.Equals(sortDirection, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            var orderLookup = lookups.FirstOrDefault(lookup => lookup.Field.Name == orderByColumn);
            var orderExpression = orderLookup?.DisplayExpression
                ?? (orderByColumn == "1" ? "(SELECT NULL)" : $"[data].{QuoteIdentifier(orderByColumn)}");
            var tieBreakers = string.Concat(result.PrimaryKeyColumns.Where(column => column != orderByColumn || orderLookup != null)
                .Select(column => $", [data].{QuoteIdentifier(column)} {direction}"));

            var dataSql = $@"
                SELECT {columnsList}
                FROM {fromClause}
                {whereClause}
                ORDER BY {orderExpression} {direction}{tieBreakers}
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY";

            parameters.Add("Offset", offset);
            parameters.Add("PageSize", pageSize);

            var records = await connection.QueryAsync(dataSql, parameters);
            result.Records = records.Cast<IDictionary<string, object>>()
                .Select(row => row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value))
                .ToList();
            foreach (var lookup in lookups)
            {
                var labels = new Dictionary<string, string>();
                foreach (var record in result.Records)
                {
                    if (record.TryGetValue(lookup.Field.Name, out var value) && value != null && record[lookup.ResultColumn] is string label)
                        labels[Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!] = label;
                    record.Remove(lookup.ResultColumn);
                }
                result.LookupLabels[lookup.Field.Name] = labels;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting data for form definition {FormDefinitionId}", formDefinitionId);
            throw;
        }
    }

    public async Task<List<Dictionary<string, object?>>> GetSelectedRecordsAsync(int formDefinitionId, List<string> recordIds)
    {
        var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formDefinitionId);
        if (formDefinition == null)
        {
            throw new ArgumentException($"Form definition with ID {formDefinitionId} not found");
        }

        if (!recordIds.Any())
        {
            return new List<Dictionary<string, object?>>();
        }

        try
        {
            var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                formDefinition.DatabaseConnectionName,
                formDefinition.TableName,
                formDefinition.Schema);

            if (!primaryKeyColumns.Any())
            {
                throw new InvalidOperationException("No primary key found for the table");
            }

            // For simplicity, assume single primary key column
            var primaryKeyColumn = primaryKeyColumns.First();
            var visibleFields = formDefinition.Fields.Where(f => f.IsVisibleInDataView).OrderBy(f => f.DisplayOrder).ToList();
            var columnsList = string.Join(", ", visibleFields.Select(f => $"[{f.Name}]"));

            var sql = $@"
                SELECT {columnsList}
                FROM [{formDefinition.Schema}].[{formDefinition.TableName}]
                WHERE [{primaryKeyColumn}] IN @RecordIds";

            using var connection = new SqlConnection(GetConnectionString(formDefinition.DatabaseConnectionName));
            await connection.OpenAsync();

            var records = await connection.QueryAsync(sql, new { RecordIds = recordIds });
            return records.Cast<IDictionary<string, object>>()
                .Select(row => row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting selected records for form definition {FormDefinitionId}", formDefinitionId);
            throw;
        }
    }

    public async Task<BulkFormRequest> CreateBulkUpdateRequestAsync(int formDefinitionId, List<Dictionary<string, object?>> records, Dictionary<string, object?> updates, string userId, string userName, string? comments = null)
    {
        var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formDefinitionId);
        if (formDefinition == null)
        {
            throw new ArgumentException($"Form definition with ID {formDefinitionId} not found");
        }

        var formRequests = new List<CreateFormRequestDto>();

        foreach (var record in records)
        {
            // Create a new dictionary with updates applied
            var updatedValues = new Dictionary<string, object?>(record);
            foreach (var update in updates)
            {
                updatedValues[update.Key] = update.Value;
            }

            var formRequest = new CreateFormRequestDto
            {
                FormDefinitionId = formDefinitionId,
                RequestType = RequestType.Update,
                FieldValues = updatedValues,
                OriginalValues = record,
                Comments = comments
            };

            formRequests.Add(formRequest);
        }

        var createDto = new CreateBulkFormRequestDto
        {
            FormDefinitionId = formDefinitionId,
            RequestType = RequestType.Update,
            FileName = $"bulk_update_{DateTime.Now:yyyyMMdd_HHmmss}.generated",
            FormRequests = formRequests,
            Comments = comments
        };

        return await _bulkFormRequestService.CreateBulkFormRequestAsync(createDto, userId, userName);
    }

    public async Task<BulkFormRequest> CreateBulkDeleteRequestAsync(int formDefinitionId, List<Dictionary<string, object?>> records, string userId, string userName, string? comments = null)
    {
        var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formDefinitionId);
        if (formDefinition == null)
        {
            throw new ArgumentException($"Form definition with ID {formDefinitionId} not found");
        }

        var formRequests = new List<CreateFormRequestDto>();

        foreach (var record in records)
        {
            var formRequest = new CreateFormRequestDto
            {
                FormDefinitionId = formDefinitionId,
                RequestType = RequestType.Delete,
                FieldValues = new Dictionary<string, object?>(), // Empty for deletes
                OriginalValues = record,
                Comments = comments
            };

            formRequests.Add(formRequest);
        }

        var createDto = new CreateBulkFormRequestDto
        {
            FormDefinitionId = formDefinitionId,
            RequestType = RequestType.Delete,
            FileName = $"bulk_delete_{DateTime.Now:yyyyMMdd_HHmmss}.generated",
            FormRequests = formRequests,
            Comments = comments
        };

        return await _bulkFormRequestService.CreateBulkFormRequestAsync(createDto, userId, userName);
    }

    private string GetConnectionString(string connectionName)
    {
        // First check in our dictionary of database connections
        if (_connectionStrings.TryGetValue(connectionName, out var connectionString))
        {
            return connectionString;
        }
        
        // If not found, try standard connection strings
        connectionString = _configuration.GetConnectionString(connectionName);
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning($"Connection string '{connectionName}' not found. Falling back to default connection.");
            return _defaultConnectionString; // Fall back to default connection
        }
        return connectionString;
    }

    /// <summary>
    /// Splits a search string into individual terms. Words separated by spaces are
    /// treated as independent terms (AND). Phrases enclosed in double quotes are
    /// kept as a single term.
    /// </summary>
    private static List<string> ParseSearchTerms(string input)
    {
        var terms = new List<string>();
        var span = input.AsSpan().Trim();
        int i = 0;

        while (i < span.Length)
        {
            // Skip whitespace
            while (i < span.Length && char.IsWhiteSpace(span[i])) i++;
            if (i >= span.Length) break;

            if (span[i] == '"')
            {
                // Quoted phrase — find closing quote
                i++; // skip opening quote
                int start = i;
                while (i < span.Length && span[i] != '"') i++;
                var phrase = span[start..i].Trim();
                if (phrase.Length > 0)
                    terms.Add(phrase.ToString());
                if (i < span.Length) i++; // skip closing quote
            }
            else
            {
                // Unquoted word — read until whitespace or quote
                int start = i;
                while (i < span.Length && !char.IsWhiteSpace(span[i]) && span[i] != '"') i++;
                var word = span[start..i].Trim();
                if (word.Length > 0)
                    terms.Add(word.ToString());
            }
        }

        return terms;
    }
}
