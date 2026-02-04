using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using System.Text.Json;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Result of applying form request changes to the database.
/// </summary>
public class ApplicationResult
{
    public bool Success { get; set; }
    public string? RecordKey { get; set; }
    public string? ErrorMessage { get; set; }

    public static ApplicationResult Succeeded(string? recordKey) => new()
    {
        Success = true,
        RecordKey = recordKey
    };

    public static ApplicationResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Implementation of form request application operations (applying changes to target database).
/// </summary>
public class FormRequestApplicationService : IFormRequestApplicationService
{
    private readonly IFormRequestRepository _formRequestRepository;
    private readonly IFormRequestHistoryService _historyService;
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly IDataService _dataService;
    private readonly IAdvancedNotificationService _notificationService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FormRequestApplicationService> _logger;

    public FormRequestApplicationService(
        IFormRequestRepository formRequestRepository,
        IFormRequestHistoryService historyService,
        IFormDefinitionService formDefinitionService,
        IDataService dataService,
        IAdvancedNotificationService notificationService,
        IDbConnectionFactory connectionFactory,
        ILogger<FormRequestApplicationService> logger)
    {
        _formRequestRepository = formRequestRepository;
        _historyService = historyService;
        _formDefinitionService = formDefinitionService;
        _dataService = dataService;
        _notificationService = notificationService;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<bool> ApplyAsync(int formRequestId)
    {
        try
        {
            var formRequest = await _formRequestRepository.GetByIdAsync(formRequestId);
            if (formRequest == null || formRequest.Status != RequestStatus.Approved)
            {
                return false;
            }

            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formRequest.FormDefinitionId);
            if (formDefinition == null)
            {
                return false;
            }

            var result = await ApplyChangesToDatabaseAsync(formRequest);

            if (result.Success)
            {
                await _formRequestRepository.UpdateStatusAsync(formRequestId, RequestStatus.Applied, null);
                await _formRequestRepository.SetAppliedRecordKeyAsync(formRequestId, result.RecordKey);

                await _historyService.RecordChangeAsync(
                    formRequestId,
                    FormRequestChangeType.Applied,
                    new Dictionary<string, object?> { { "Status", "Approved" } },
                    new Dictionary<string, object?> { { "Status", "Applied" } },
                    "System",
                    "System",
                    "Request applied to target database"
                );

                // Send notification to requestor
                await SendRequestAppliedNotificationAsync(formRequest, formDefinition);
            }
            else
            {
                _logger.LogWarning("Data operation failed for form request {Id}", formRequestId);
                
                await _formRequestRepository.UpdateStatusAsync(formRequestId, RequestStatus.Failed, 
                    result.ErrorMessage ?? "Data operation failed - check target database constraints and permissions");

                await _historyService.RecordChangeAsync(
                    formRequestId,
                    FormRequestChangeType.Failed,
                    new Dictionary<string, object?> { { "Status", "Approved" } },
                    new Dictionary<string, object?> { { "Status", "Failed" } },
                    "System",
                    "System",
                    "Request failed to apply to target database"
                );
            }

            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying form request {Id}", formRequestId);
            
            try
            {
                await _formRequestRepository.UpdateStatusAsync(formRequestId, RequestStatus.Failed, 
                    $"Error applying request: {ex.Message}");
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Failed to update status to Failed for form request {Id}", formRequestId);
            }
            
            throw;
        }
    }

    public async Task<ApplicationResult> ApplyChangesToDatabaseAsync(FormRequest formRequest)
    {
        var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formRequest.FormDefinitionId);
        if (formDefinition == null)
        {
            return ApplicationResult.Failed("Form definition not found");
        }

        return await ApplyChangesToDatabaseAsync(formRequest, null, null);
    }

    public async Task<ApplicationResult> ApplyChangesToDatabaseAsync(
        FormRequest formRequest, SqlConnection? connection, SqlTransaction? transaction)
    {
        try
        {
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formRequest.FormDefinitionId);
            if (formDefinition == null)
            {
                return ApplicationResult.Failed($"Form definition with ID {formRequest.FormDefinitionId} not found");
            }

            var convertedFieldValues = ConvertJsonElementsToValues(formRequest.FieldValues);
            var convertedOriginalValues = ConvertJsonElementsToValues(formRequest.OriginalValues);

            bool success;
            object? recordKey = null;

            switch (formRequest.RequestType)
            {
                case RequestType.Insert:
                    var insertResult = await _dataService.InsertDataWithIdAsync(
                        formDefinition.DatabaseConnectionName,
                        formDefinition.TableName,
                        formDefinition.Schema,
                        convertedFieldValues
                    );
                    success = insertResult.Success;
                    recordKey = insertResult.InsertedId;
                    break;

                case RequestType.Update:
                    var updateWhereConditions = await BuildWhereConditionsAsync(
                        formDefinition, convertedOriginalValues);
                    
                    success = await _dataService.UpdateDataAsync(
                        formDefinition.DatabaseConnectionName,
                        formDefinition.TableName,
                        formDefinition.Schema,
                        convertedFieldValues,
                        updateWhereConditions
                    );
                    
                    if (success)
                    {
                        recordKey = string.Join(", ", updateWhereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    }
                    break;

                case RequestType.Delete:
                    var deleteWhereConditions = await BuildWhereConditionsAsync(
                        formDefinition, convertedOriginalValues);
                    
                    success = await _dataService.DeleteDataAsync(
                        formDefinition.DatabaseConnectionName,
                        formDefinition.TableName,
                        formDefinition.Schema,
                        deleteWhereConditions
                    );
                    
                    if (success)
                    {
                        recordKey = string.Join(", ", deleteWhereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                    }
                    break;

                default:
                    return ApplicationResult.Failed($"Unsupported request type: {formRequest.RequestType}");
            }

            if (success)
            {
                return ApplicationResult.Succeeded(recordKey?.ToString());
            }
            else
            {
                return ApplicationResult.Failed($"Failed to apply {formRequest.RequestType} operation");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying changes for form request {Id}", formRequest.Id);
            return ApplicationResult.Failed(ex.Message);
        }
    }

    public async Task<bool> ManuallyApplyAsync(int formRequestId)
    {
        try
        {
            _logger.LogInformation("Manually applying form request {FormRequestId}", formRequestId);
            
            var formRequest = await _formRequestRepository.GetByIdAsync(formRequestId);
            if (formRequest == null || formRequest.Status != RequestStatus.Approved)
            {
                _logger.LogWarning("Form request {FormRequestId} not found or not in approved status", formRequestId);
                return false;
            }

            var success = await ApplyAsync(formRequestId);
            
            if (success)
            {
                _logger.LogInformation("Successfully manually applied form request {FormRequestId}", formRequestId);
            }
            else
            {
                _logger.LogError("Failed to manually apply form request {FormRequestId}", formRequestId);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error manually applying form request {FormRequestId}", formRequestId);
            return false;
        }
    }

    private async Task<Dictionary<string, object?>> BuildWhereConditionsAsync(
        FormDefinition formDefinition, Dictionary<string, object?> originalValues)
    {
        var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
            formDefinition.DatabaseConnectionName,
            formDefinition.TableName,
            formDefinition.Schema
        );

        if (!primaryKeyColumns.Any())
        {
            throw new InvalidOperationException(
                $"No primary key found for table {formDefinition.Schema}.{formDefinition.TableName}");
        }

        var whereConditions = new Dictionary<string, object?>();
        foreach (var pkColumn in primaryKeyColumns)
        {
            if (originalValues.ContainsKey(pkColumn))
            {
                whereConditions[pkColumn] = originalValues[pkColumn];
            }
            else
            {
                throw new InvalidOperationException(
                    $"Primary key column '{pkColumn}' not found in original values");
            }
        }

        return whereConditions;
    }

    private Dictionary<string, object?> ConvertJsonElementsToValues(Dictionary<string, object?> data)
    {
        var result = new Dictionary<string, object?>();
        
        foreach (var kvp in data)
        {
            if (kvp.Value is JsonElement jsonElement)
            {
                result[kvp.Key] = ConvertJsonElement(jsonElement);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        
        return result;
    }

    private object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => ConvertStringValue(element.GetString()),
            JsonValueKind.Number => element.TryGetInt32(out int intValue) ? intValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    private static object? ConvertStringValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (DateTime.TryParse(value, out var dateTime))
        {
            return dateTime;
        }

        if (DateTimeOffset.TryParse(value, out var dateTimeOffset))
        {
            return dateTimeOffset.DateTime;
        }

        return value;
    }

    private async Task SendRequestAppliedNotificationAsync(FormRequest formRequest, FormDefinition formDefinition)
    {
        try
        {
            // TODO: Implement notification sending using IAdvancedNotificationService
            _logger.LogInformation("Notification would be sent for applied form request {Id}", formRequest.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification for applied form request {Id}", formRequest.Id);
        }
    }
}
