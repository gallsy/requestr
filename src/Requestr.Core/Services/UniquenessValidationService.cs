using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;

namespace Requestr.Core.Services;

public class UniquenessValidationService : IUniquenessValidationService
{
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UniquenessValidationService> _logger;

    public UniquenessValidationService(
        IFormDefinitionService formDefinitionService,
        IDbConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<UniquenessValidationService> logger)
    {
        _formDefinitionService = formDefinitionService;
        _connectionFactory = connectionFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<string>> ValidateAsync(int formDefinitionId, Dictionary<string, object?> fieldValues, RequestType requestType, Dictionary<string, object?>? originalValues = null)
    {
        var violations = new List<string>();

        // Delete requests don't need uniqueness validation
        if (requestType == RequestType.Delete)
            return violations;

        var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formDefinitionId);
        if (formDefinition == null)
            return violations;

        var uniqueFields = formDefinition.Fields.Where(f => f.IsUnique && f.IsVisible).ToList();
        if (!uniqueFields.Any())
            return violations;

        // Check each unique field
        foreach (var field in uniqueFields)
        {
            if (!fieldValues.TryGetValue(field.Name, out var value) || value == null || string.IsNullOrEmpty(value.ToString()))
                continue;

            // For updates, skip validation if the unique field value hasn't changed
            if (requestType == RequestType.Update && originalValues != null)
            {
                if (originalValues.TryGetValue(field.Name, out var originalValue) &&
                    string.Equals(value?.ToString(), originalValue?.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            // Check target table for existing records
            var existsInTable = await CheckExistsInTargetTableAsync(formDefinition, field.Name, value!);
            if (existsInTable)
            {
                violations.Add($"The value '{value}' already exists in '{field.DisplayName}'. This field requires unique values.");
                continue;
            }

            // Check pending/approved requests for conflicts
            var existsInPending = await CheckExistsInPendingRequestsAsync(formDefinitionId, field.Name, value!);
            if (existsInPending)
            {
                violations.Add($"Another pending request already uses the value '{value}' for '{field.DisplayName}'.");
            }
        }

        return violations;
    }

    private async Task<bool> CheckExistsInTargetTableAsync(FormDefinition formDefinition, string columnName, object value)
    {
        try
        {
            var connectionString = GetTargetConnectionString(formDefinition.DatabaseConnectionName);
            if (string.IsNullOrEmpty(connectionString))
                return false;

            // Validate column name exists in form definition to prevent SQL injection
            if (!formDefinition.Fields.Any(f => f.Name == columnName))
                return false;

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = $@"SELECT COUNT(1) FROM [{formDefinition.Schema}].[{formDefinition.TableName}] WHERE [{columnName}] = @Value";
            var count = await connection.ExecuteScalarAsync<int>(sql, new { Value = value.ToString() });
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking uniqueness in target table for {Column} in {Schema}.{Table}",
                columnName, formDefinition.Schema, formDefinition.TableName);
            return false; // Fail open - don't block submission if we can't check
        }
    }

    private async Task<bool> CheckExistsInPendingRequestsAsync(int formDefinitionId, string fieldName, object value)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            // Check pending/approved (not yet applied) requests that have the same value for this field
            var sql = @"
                SELECT COUNT(1) 
                FROM FormRequests 
                WHERE FormDefinitionId = @FormDefinitionId
                    AND Status IN (@Pending, @Approved)
                    AND RequestType IN (@Insert, @Update)
                    AND JSON_VALUE(FieldValues, @JsonPath) = @Value";

            var count = await connection.ExecuteScalarAsync<int>(sql, new
            {
                FormDefinitionId = formDefinitionId,
                Pending = (int)RequestStatus.Pending,
                Approved = (int)RequestStatus.Approved,
                Insert = (int)RequestType.Insert,
                Update = (int)RequestType.Update,
                JsonPath = $"$.{fieldName}",
                Value = value.ToString()
            });

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking uniqueness in pending requests for field {Field} in form {FormId}",
                fieldName, formDefinitionId);
            return false; // Fail open
        }
    }

    private string? GetTargetConnectionString(string connectionStringName)
    {
        var connectionString = _configuration.GetConnectionString(connectionStringName);
        if (!string.IsNullOrEmpty(connectionString))
            return connectionString;

        connectionString = _configuration.GetSection($"DatabaseConnections:{connectionStringName}").Value;
        if (!string.IsNullOrEmpty(connectionString))
            return connectionString;

        return null;
    }
}
