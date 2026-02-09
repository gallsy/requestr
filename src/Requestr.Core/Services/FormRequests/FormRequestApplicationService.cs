using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using Requestr.Core.Services.Workflow;
using Requestr.Core.Utilities;

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
    private readonly IWorkflowProgressService _workflowProgressService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FormRequestApplicationService> _logger;

    public FormRequestApplicationService(
        IFormRequestRepository formRequestRepository,
        IFormRequestHistoryService historyService,
        IFormDefinitionService formDefinitionService,
        IDataService dataService,
        IAdvancedNotificationService notificationService,
        IWorkflowProgressService workflowProgressService,
        IDbConnectionFactory connectionFactory,
        IConfiguration configuration,
        ILogger<FormRequestApplicationService> logger)
    {
        _formRequestRepository = formRequestRepository;
        _historyService = historyService;
        _formDefinitionService = formDefinitionService;
        _dataService = dataService;
        _notificationService = notificationService;
        _workflowProgressService = workflowProgressService;
        _connectionFactory = connectionFactory;
        _configuration = configuration;
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

            var convertedFieldValues = SqlTypeConverter.ConvertDictionary(formRequest.FieldValues, formDefinition.Fields);
            var convertedOriginalValues = SqlTypeConverter.ConvertDictionary(formRequest.OriginalValues, formDefinition.Fields);

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

    public async Task<string> GetWorkflowDiagnosticsAsync(int formRequestId)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            var diagnostics = new List<string>();

            var formRequest = await _formRequestRepository.GetByIdAsync(formRequestId);
            if (formRequest == null)
            {
                return "Form request not found.";
            }

            diagnostics.Add($"Form Request {formRequestId} Diagnostics:");
            diagnostics.Add($"  Status: {formRequest.Status}");
            diagnostics.Add($"  Requested At: {formRequest.RequestedAt}");
            diagnostics.Add($"  Workflow Instance ID: {formRequest.WorkflowInstanceId}");

            if (formRequest.WorkflowInstanceId.HasValue)
            {
                var workflowSql = @"
                    SELECT wi.*, wd.Name as WorkflowName
                    FROM WorkflowInstances wi
                    INNER JOIN WorkflowDefinitions wd ON wi.WorkflowDefinitionId = wd.Id
                    WHERE wi.Id = @WorkflowInstanceId";

                var workflowData = await connection.QueryFirstOrDefaultAsync(workflowSql, new { WorkflowInstanceId = formRequest.WorkflowInstanceId });
                
                if (workflowData != null)
                {
                    diagnostics.Add($"  Workflow: {workflowData.WorkflowName}");
                    
                    var statusValue = workflowData.Status;
                    string statusName;
                    if (int.TryParse(statusValue?.ToString(), out int statusInt))
                    {
                        statusName = Enum.GetName(typeof(WorkflowInstanceStatus), statusInt) ?? statusValue?.ToString() ?? "Unknown";
                    }
                    else
                    {
                        statusName = statusValue?.ToString() ?? "Unknown";
                    }
                    
                    diagnostics.Add($"  Workflow Status: {statusName}");
                    diagnostics.Add($"  Current Step: {workflowData.CurrentStepId}");
                    diagnostics.Add($"  Started At: {workflowData.StartedAt}");
                    diagnostics.Add($"  Completed At: {workflowData.CompletedAt}");

                    var stepsSql = @"
                        SELECT wsi.*, ws.Name as StepName, ws.StepType
                        FROM WorkflowStepInstances wsi
                        INNER JOIN WorkflowSteps ws ON wsi.StepId = ws.StepId AND ws.WorkflowDefinitionId = @WorkflowDefinitionId
                        WHERE wsi.WorkflowInstanceId = @WorkflowInstanceId
                        ORDER BY wsi.StartedAt";

                    var steps = await connection.QueryAsync(stepsSql, new 
                    { 
                        WorkflowInstanceId = formRequest.WorkflowInstanceId,
                        WorkflowDefinitionId = workflowData.WorkflowDefinitionId
                    });

                    diagnostics.Add("  Workflow Steps:");
                    foreach (var step in steps)
                    {
                        var stepStatusValue = step.Status;
                        string stepStatusName;
                        if (int.TryParse(stepStatusValue?.ToString(), out int stepStatusInt))
                        {
                            stepStatusName = Enum.GetName(typeof(WorkflowStepInstanceStatus), stepStatusInt) ?? stepStatusValue?.ToString() ?? "Unknown";
                        }
                        else
                        {
                            stepStatusName = stepStatusValue?.ToString() ?? "Unknown";
                        }
                        
                        diagnostics.Add($"    - {step.StepName} ({step.StepType}): {stepStatusName}");
                        if (step.CompletedAt != null)
                        {
                            diagnostics.Add($"      Completed: {step.CompletedAt} by {step.CompletedByName}");
                        }
                        if (!string.IsNullOrEmpty(step.Comments))
                        {
                            diagnostics.Add($"      Comments: {step.Comments}");
                        }
                    }
                }
            }

            diagnostics.Add("  Potential Issues:");
            if (formRequest.WorkflowInstanceId.HasValue && formRequest.Status == RequestStatus.Approved)
            {
                var workflowProgress = await _workflowProgressService.GetWorkflowProgressAsync(formRequestId);
                if (workflowProgress?.Status == WorkflowInstanceStatus.Completed)
                {
                    diagnostics.Add("    - Workflow is complete but request is not applied to database");
                    diagnostics.Add("    - This request should be processed manually");
                }
            }

            if (!string.IsNullOrEmpty(formRequest.FailureMessage))
            {
                diagnostics.Add($"    - Failure Message: {formRequest.FailureMessage}");
            }

            return string.Join(Environment.NewLine, diagnostics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workflow diagnostics for form request {FormRequestId}", formRequestId);
            return $"Error getting diagnostics: {ex.Message}";
        }
    }

    private async Task SendRequestAppliedNotificationAsync(FormRequest formRequest, FormDefinition formDefinition)
    {
        try
        {
            // Get the requestor's email from the Users table
            string? requestorEmail = null;
            using (var connection = await _connectionFactory.CreateConnectionAsync())
            {
                var sql = @"SELECT Email FROM Users WHERE Id = @UserId OR Email = @UserId";
                requestorEmail = await connection.QueryFirstOrDefaultAsync<string>(sql, new { UserId = formRequest.RequestedBy });
            }

            if (string.IsNullOrEmpty(requestorEmail))
            {
                if (formRequest.RequestedBy?.Contains("@") == true)
                {
                    requestorEmail = formRequest.RequestedBy;
                }
                else
                {
                    _logger.LogWarning("Could not determine email for requestor {RequestedBy} for request {RequestId}",
                        formRequest.RequestedBy, formRequest.Id);
                    return;
                }
            }

            var baseUrl = _configuration["Branding:BaseUrl"] ?? "http://localhost:8080";
            var systemName = _configuration["Branding:ApplicationName"] ?? "Requestr";

            var variables = new Dictionary<string, string>
            {
                { "{{RequestId}}", formRequest.Id.ToString() },
                { "{{FormName}}", formDefinition.Name },
                { "{{RequestType}}", formRequest.RequestType.ToString() },
                { "{{RequestorName}}", formRequest.RequestedByName ?? formRequest.RequestedBy },
                { "{{RequestCreatedDate}}", formRequest.RequestedAt.ToString("yyyy-MM-dd HH:mm:ss") },
                { "{{AppliedDate}}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                { "{{RequestComments}}", formRequest.Comments ?? "" },
                { "{{RequestUrl}}", $"{baseUrl}/admin/requests/details/{formRequest.Id}" },
                { "{{SystemName}}", systemName }
            };

            if (formRequest.FieldValues != null)
            {
                foreach (var field in formRequest.FieldValues)
                {
                    variables[$"{{{{Field_{field.Key}}}}}"] = field.Value?.ToString() ?? "";
                }
            }

            await _notificationService.SendNotificationAsync(
                NotificationTemplateKeys.RequestApproved,
                variables,
                requestorEmail);

            _logger.LogInformation("Sent request approved notification for request {RequestId} to {Email}",
                formRequest.Id, requestorEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending request applied notification for request {RequestId}", formRequest.Id);
            // Don't throw - notification failures shouldn't break the request application
        }
    }
}
