using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;

namespace Requestr.Core.Services;

public class DataViewService : IDataViewService
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

    public async Task<DataViewResult> GetDataAsync(int formDefinitionId, int page = 1, int pageSize = 50, string? searchTerm = null, Dictionary<string, object?>? filters = null)
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

            // Build query with pagination and filtering
            var whereConditions = new List<string>();
            var parameters = new DynamicParameters();

            // Add search functionality
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var searchConditions = new List<string>();
                foreach (var field in visibleFields.Where(f => f.DataType == "string" || f.DataType == "text"))
                {
                    searchConditions.Add($"[{field.Name}] LIKE @SearchTerm");
                }
                if (searchConditions.Any())
                {
                    whereConditions.Add($"({string.Join(" OR ", searchConditions)})");
                    parameters.Add("SearchTerm", $"%{searchTerm}%");
                }
            }

            // Add filters
            if (filters != null && filters.Any())
            {
                foreach (var filter in filters)
                {
                    whereConditions.Add($"[{filter.Key}] = @{filter.Key}");
                    parameters.Add(filter.Key, filter.Value);
                }
            }

            var whereClause = whereConditions.Any() ? $"WHERE {string.Join(" AND ", whereConditions)}" : "";

            // Get total count
            var countSql = $@"
                SELECT COUNT(*)
                FROM [{formDefinition.Schema}].[{formDefinition.TableName}]
                {whereClause}";

            using var connection = new SqlConnection(GetConnectionString(formDefinition.DatabaseConnectionName));
            await connection.OpenAsync();

            result.TotalCount = await connection.QuerySingleAsync<int>(countSql, parameters);
            result.TotalPages = (int)Math.Ceiling((double)result.TotalCount / pageSize);

            // Get paginated data
            var offset = (page - 1) * pageSize;
            var columnsList = string.Join(", ", result.Columns.Select(c => $"[{c}]"));
            var orderByColumn = result.PrimaryKeyColumns.FirstOrDefault() ?? result.Columns.FirstOrDefault() ?? "1";

            var dataSql = $@"
                SELECT {columnsList}
                FROM [{formDefinition.Schema}].[{formDefinition.TableName}]
                {whereClause}
                ORDER BY [{orderByColumn}]
                OFFSET @Offset ROWS
                FETCH NEXT @PageSize ROWS ONLY";

            parameters.Add("Offset", offset);
            parameters.Add("PageSize", pageSize);

            var records = await connection.QueryAsync(dataSql, parameters);
            result.Records = records.Cast<IDictionary<string, object>>()
                .Select(row => row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value))
                .ToList();

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
}
