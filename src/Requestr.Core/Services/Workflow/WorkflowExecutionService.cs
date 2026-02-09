using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service implementation for executing workflow steps and processing actions.
/// </summary>
public class WorkflowExecutionService : IWorkflowExecutionService
{
    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly IWorkflowStepInstanceRepository _stepInstanceRepository;
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IDataService _dataService;
    private readonly INotificationService _notificationService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkflowExecutionService> _logger;

    private const string RequestApproved = "REQUEST_APPROVED";
    private const string RequestRejected = "REQUEST_REJECTED";
    private const string WorkflowStepPending = "WORKFLOW_STEP_PENDING";

    public WorkflowExecutionService(
        IWorkflowInstanceRepository instanceRepository,
        IWorkflowStepInstanceRepository stepInstanceRepository,
        IWorkflowDefinitionRepository definitionRepository,
        IDbConnectionFactory connectionFactory,
        IDataService dataService,
        INotificationService notificationService,
        IConfiguration configuration,
        ILogger<WorkflowExecutionService> logger)
    {
        _instanceRepository = instanceRepository;
        _stepInstanceRepository = stepInstanceRepository;
        _definitionRepository = definitionRepository;
        _connectionFactory = connectionFactory;
        _dataService = dataService;
        _notificationService = notificationService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteStepAsync(
        int workflowInstanceId,
        string stepId,
        string completedBy,
        string completedByName,
        WorkflowStepAction action,
        string? comments,
        Dictionary<string, object?>? fieldValues)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Get the workflow instance
                var instance = await _instanceRepository.GetByIdAsync(workflowInstanceId, connection, transaction);
                if (instance == null)
                {
                    _logger.LogWarning("Workflow instance {InstanceId} not found", workflowInstanceId);
                    return false;
                }

                // Verify this is the current step
                if (!instance.CurrentStepId.Equals(stepId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Step {StepId} is not the current step ({CurrentStepId}) for workflow {InstanceId}",
                        stepId, instance.CurrentStepId, workflowInstanceId);
                    return false;
                }

                // Get the workflow definition
                var definition = await _definitionRepository.GetByIdAsync(instance.WorkflowDefinitionId, connection, transaction);
                if (definition == null)
                {
                    _logger.LogError("Workflow definition {DefinitionId} not found", instance.WorkflowDefinitionId);
                    return false;
            }

            // Complete the step
            await _stepInstanceRepository.UpdateToCompletedAsync(
                workflowInstanceId,
                stepId,
                completedBy,
                action,
                comments,
                fieldValues,
                connection,
                transaction);

            _logger.LogInformation("Completed step {StepId} with action {Action} for workflow {InstanceId}",
                stepId, action, workflowInstanceId);

            // Handle the action
            if (action == WorkflowStepAction.Rejected)
            {
                await RejectWorkflowAsync((SqlConnection)connection, (SqlTransaction)transaction, workflowInstanceId, completedBy, comments, instance.FormRequestId);
            }
            else
            {
                // Move to next step
                var nextStepId = await GetNextStepIdAsync(definition, stepId);
                if (!string.IsNullOrEmpty(nextStepId))
                {
                    await MoveToNextStepAsync((SqlConnection)connection, (SqlTransaction)transaction, workflowInstanceId, nextStepId, definition, completedBy);
                }
            }

            await transaction.CommitAsync();

            // Send notification (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendStepActionNotificationAsync(instance.FormRequestId, stepId, 
                        definition.Steps.FirstOrDefault(s => s.StepId == stepId)?.Name ?? stepId, 
                        action, completedBy, completedByName, comments);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending step action notification");
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to complete step {StepId} for workflow {InstanceId}", stepId, workflowInstanceId);
            throw;
        }
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowActionResult> ProcessWorkflowActionAsync(
        int workflowInstanceId,
        string actionType,
        string userId,
        string? comments,
        Dictionary<string, object?>? fieldUpdates)
    {
        try
        {
            var currentStep = await GetCurrentStepInstanceAsync(workflowInstanceId);
            if (currentStep == null)
            {
                return new WorkflowActionResult 
                { 
                    Success = false, 
                    Message = "No current step found for workflow instance"
                };
            }

            var action = actionType.ToLower() switch
            {
                "approve" => WorkflowStepAction.Approved,
                "reject" => WorkflowStepAction.Rejected,
                "complete" => WorkflowStepAction.Completed,
                _ => throw new ArgumentException($"Unknown action type: {actionType}")
            };

            var stepCompleted = await CompleteStepAsync(
                workflowInstanceId, 
                currentStep.StepId, 
                userId, 
                userId, 
                action, 
                comments, 
                fieldUpdates);
            
            if (stepCompleted)
            {
                // Check if workflow is now complete
                var workflowInstance = await _instanceRepository.GetByIdAsync(workflowInstanceId);
                var workflowCompleted = workflowInstance?.Status == WorkflowInstanceStatus.Completed || 
                                       workflowInstance?.Status == WorkflowInstanceStatus.Failed;
                var workflowApproved = workflowInstance?.Status == WorkflowInstanceStatus.Completed && 
                                      action == WorkflowStepAction.Approved;
                var workflowRejected = workflowInstance?.Status == WorkflowInstanceStatus.Failed;

                return new WorkflowActionResult
                {
                    Success = true,
                    Message = workflowRejected ? "Workflow rejected" : $"Step {action.ToString().ToLower()} successfully",
                    WorkflowCompleted = workflowCompleted,
                    WorkflowApproved = workflowApproved,
                    PreviousStepName = currentStep.StepId,
                    CurrentStepName = workflowInstance?.CurrentStepId,
                    ActorName = userId,
                    AdditionalData = workflowRejected ? new Dictionary<string, object?> { ["rejected"] = true } : new Dictionary<string, object?>()
                };
            }

            return new WorkflowActionResult { Success = false, Message = "Failed to complete step" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing workflow action {ActionType} for instance {WorkflowInstanceId}", actionType, workflowInstanceId);
            return new WorkflowActionResult 
            { 
                Success = false, 
                Message = $"Error processing action: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<List<WorkflowStepInstance>> GetPendingStepsForUserAsync(string userId, List<string> userRoles)
    {
        return await _stepInstanceRepository.GetPendingForUserAsync(userId, userRoles);
    }

    /// <inheritdoc />
    public async Task<WorkflowStepInstance?> GetCurrentStepInstanceAsync(int workflowInstanceId)
    {
        return await _stepInstanceRepository.GetCurrentAsync(workflowInstanceId);
    }

    /// <inheritdoc />
    public async Task<List<WorkflowStepInstance>> GetStepInstancesAsync(int workflowInstanceId)
    {
        return await _stepInstanceRepository.GetByWorkflowInstanceIdAsync(workflowInstanceId);
    }

    /// <inheritdoc />
    public async Task<WorkflowStepInstance?> GetCurrentWorkflowStepAsync(int workflowInstanceId)
    {
        return await _stepInstanceRepository.GetCurrentAsync(workflowInstanceId);
    }

    /// <inheritdoc />
    public async Task<List<WorkflowStepInstance>> GetCompletedWorkflowStepsAsync(int workflowInstanceId)
    {
        return await _stepInstanceRepository.GetCompletedAsync(workflowInstanceId);
    }

    /// <inheritdoc />
    public async Task<bool> CanUserAccessStepAsync(int workflowInstanceId, string stepId, string userId, List<string> userRoles)
    {
        var instance = await _instanceRepository.GetByIdAsync(workflowInstanceId);
        if (instance == null) return false;

        var definition = await _definitionRepository.GetByIdAsync(instance.WorkflowDefinitionId);
        if (definition == null) return false;

        var step = definition.Steps.FirstOrDefault(s => s.StepId == stepId);
        if (step == null) return false;

        // If no roles assigned, anyone can access
        if (step.AssignedRoles == null || step.AssignedRoles.Count == 0)
            return true;

        // Check if user has any of the required roles
        return userRoles.Any(role => step.AssignedRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<List<string>> GetAvailableStepsForUserAsync(int workflowInstanceId, string userId, List<string> userRoles)
    {
        var availableSteps = new List<string>();
        var instance = await _instanceRepository.GetByIdAsync(workflowInstanceId);
        if (instance == null) return availableSteps;

        var definition = await _definitionRepository.GetByIdAsync(instance.WorkflowDefinitionId);
        if (definition == null) return availableSteps;

        foreach (var step in definition.Steps)
        {
            if (await CanUserAccessStepAsync(workflowInstanceId, step.StepId, userId, userRoles))
            {
                availableSteps.Add(step.StepId);
            }
        }

        return availableSteps;
    }

    private async Task<string?> GetNextStepIdAsync(WorkflowDefinition definition, string currentStepId)
    {
        var transitions = definition.Transitions
            .Where(t => t.FromStepId.Equals(currentStepId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!transitions.Any())
        {
            _logger.LogDebug("No transitions found from step {StepId}", currentStepId);
            return null;
        }

        // Return the first transition's target (conditions can be added later)
        return transitions.First().ToStepId;
    }

    private async Task MoveToNextStepAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int workflowInstanceId,
        string nextStepId,
        WorkflowDefinition definition,
        string completedBy)
    {
        // Update workflow instance current step
        await _instanceRepository.UpdateCurrentStepAsync(workflowInstanceId, nextStepId, connection, transaction);

        // Update step instance to in-progress
        await _stepInstanceRepository.UpdateToInProgressAsync(workflowInstanceId, nextStepId, connection, transaction);

        // Check if the next step is an End step
        var nextStep = definition.Steps.FirstOrDefault(s => s.StepId == nextStepId);
        if (nextStep?.StepType == WorkflowStepType.End)
        {
            await AutoCompleteEndStepAsync(connection, transaction, workflowInstanceId, nextStepId, completedBy);
        }
        else
        {
            // Send notification for the new active step (within current connection/transaction)
            try
            {
                await SendStepBecomesActiveNotificationAsync(connection, transaction, workflowInstanceId, nextStepId);
            }
            catch (Exception ex)
            {
                // Don't fail the workflow progression if notification fails
                _logger.LogError(ex, "Error sending step active notification for workflow {InstanceId}, step {StepId}",
                    workflowInstanceId, nextStepId);
            }
        }

        _logger.LogDebug("Moved workflow {InstanceId} to step {StepId}", workflowInstanceId, nextStepId);
    }

    private async Task AutoCompleteEndStepAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int workflowInstanceId,
        string stepId,
        string completedBy)
    {
        await _stepInstanceRepository.UpdateToCompletedAsync(
            workflowInstanceId,
            stepId,
            "System",
            WorkflowStepAction.Completed,
            "Auto-completed End step",
            null,
            connection,
            transaction);

        await _instanceRepository.UpdateToCompletedAsync(workflowInstanceId, completedBy, connection, transaction);

        // Get the form request ID and approve/apply the form request
        var instance = await _instanceRepository.GetByIdAsync(workflowInstanceId, connection, transaction);
        if (instance != null)
        {
            await ApproveFormRequestAsync(connection, transaction, instance.FormRequestId, completedBy);

            // Send workflow completed notification (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendWorkflowCompletedNotificationAsync(instance.FormRequestId, completedBy, true, null);
                    // Apply data changes in background
                    await ApplyWorkflowDataChangesAsync(workflowInstanceId, completedBy);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in post-workflow completion tasks");
                }
            });
        }

        _logger.LogInformation("Workflow {InstanceId} completed at End step {StepId}", workflowInstanceId, stepId);
    }

    private async Task RejectWorkflowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int workflowInstanceId,
        string rejectedBy,
        string? rejectionReason,
        int formRequestId)
    {
        // Update workflow instance status to failed
        const string updateWorkflowSql = @"
            UPDATE WorkflowInstances 
            SET Status = @Status, CompletedAt = @CompletedAt, CompletedBy = @CompletedBy 
            WHERE Id = @Id";

        await connection.ExecuteAsync(updateWorkflowSql, new
        {
            Status = (int)WorkflowInstanceStatus.Failed,
            CompletedAt = DateTime.UtcNow,
            CompletedBy = rejectedBy,
            Id = workflowInstanceId
        }, transaction);

        // Update the form request status to rejected
        const string updateRequestSql = @"
            UPDATE FormRequests 
            SET Status = @Status, RejectionReason = @RejectionReason
            WHERE Id = @FormRequestId";

        await connection.ExecuteAsync(updateRequestSql, new
        {
            Status = (int)RequestStatus.Rejected,
            RejectionReason = rejectionReason ?? "Request rejected during workflow approval",
            FormRequestId = formRequestId
        }, transaction);

        // Send rejection notification (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await SendWorkflowCompletedNotificationAsync(formRequestId, rejectedBy, false, rejectionReason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending rejection notification");
            }
        });

        _logger.LogInformation("Rejected workflow instance {WorkflowInstanceId} by user {UserId}", workflowInstanceId, rejectedBy);
    }

    private async Task ApproveFormRequestAsync(SqlConnection connection, SqlTransaction transaction, int formRequestId, string approvedBy)
    {
        const string sql = @"
            UPDATE FormRequests 
            SET Status = @Status, ApprovedBy = @ApprovedBy, ApprovedAt = @ApprovedAt
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new
        {
            Id = formRequestId,
            Status = (int)RequestStatus.Approved,
            ApprovedBy = approvedBy,
            ApprovedAt = DateTime.UtcNow
        }, transaction);

        _logger.LogInformation("Approved form request {FormRequestId}", formRequestId);
    }

    private async Task ApplyWorkflowDataChangesAsync(int workflowInstanceId, string completedBy)
    {
        int formRequestId = 0;

        try
        {
            _logger.LogInformation("Starting background data application for workflow instance {WorkflowInstanceId}", workflowInstanceId);

            using var connection = (SqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();

            // Get form request details
            const string getFormRequestSql = @"
                SELECT wi.FormRequestId, fr.Status, fr.BulkFormRequestId, 
                       fr.FieldValues, fr.OriginalValues, fr.RequestType,
                       fd.DatabaseConnectionName, fd.TableName, fd.[Schema]
                FROM WorkflowInstances wi
                INNER JOIN FormRequests fr ON wi.FormRequestId = fr.Id
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                WHERE wi.Id = @WorkflowInstanceId";

            var workflowData = await connection.QueryFirstOrDefaultAsync(getFormRequestSql, new { WorkflowInstanceId = workflowInstanceId });

            if (workflowData == null)
            {
                _logger.LogWarning("No form request found for workflow instance {WorkflowInstanceId}", workflowInstanceId);
                return;
            }

            formRequestId = workflowData.FormRequestId;
            int currentStatus = workflowData.Status;

            // Only apply if the request is in Approved status
            if (currentStatus != (int)RequestStatus.Approved)
            {
                _logger.LogInformation("Form request {FormRequestId} is not in Approved status, skipping data application", formRequestId);
                return;
            }

            int? bulkFormRequestId = workflowData.BulkFormRequestId;
            bool applicationSuccess;

            if (bulkFormRequestId.HasValue)
            {
                // In the workflow path, bulk request items are still Pending.
                // Transition them to Approved so ApplyBulkRequestItemsAsync can find and process them.
                const string approveBulkItemsSql = @"
                    UPDATE BulkFormRequestItems 
                    SET Status = @ApprovedStatus 
                    WHERE BulkFormRequestId = @BulkRequestId AND Status = @PendingStatus";

                await connection.ExecuteAsync(approveBulkItemsSql, new
                {
                    BulkRequestId = bulkFormRequestId.Value,
                    ApprovedStatus = (int)RequestStatus.Approved,
                    PendingStatus = (int)RequestStatus.Pending
                });

                applicationSuccess = await ApplyBulkRequestItemsAsync(connection, bulkFormRequestId.Value);
            }
            else
            {
                applicationSuccess = await ApplyFormRequestDataChangesAsync(workflowData);
            }

            if (applicationSuccess)
            {
                // Update status to Applied
                const string updateStatusSql = "UPDATE FormRequests SET Status = @Status WHERE Id = @Id";
                await connection.ExecuteAsync(updateStatusSql, new { Id = formRequestId, Status = (int)RequestStatus.Applied });
                _logger.LogInformation("Successfully applied data changes for form request {FormRequestId}", formRequestId);
            }
            else
            {
                _logger.LogError("Failed to apply data changes for form request {FormRequestId}", formRequestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in background data application for workflow instance {WorkflowInstanceId}", workflowInstanceId);

            if (formRequestId > 0)
            {
                try
                {
                    using var failConnection = (SqlConnection)_connectionFactory.CreateConnection();
                    await failConnection.OpenAsync();
                    const string updateFailedSql = @"
                        UPDATE FormRequests 
                        SET Status = @Status, FailureMessage = @FailureMessage 
                        WHERE Id = @Id AND Status = @ApprovedStatus";

                    await failConnection.ExecuteAsync(updateFailedSql, new
                    {
                        Id = formRequestId,
                        Status = (int)RequestStatus.Failed,
                        ApprovedStatus = (int)RequestStatus.Approved,
                        FailureMessage = $"Background data application failed: {ex.Message}"
                    });
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "Failed to update status to Failed for form request {FormRequestId}", formRequestId);
                }
            }
        }
    }

    private async Task<bool> ApplyFormRequestDataChangesAsync(dynamic requestData)
    {
        try
        {
            var fieldValues = ParseFieldValues(requestData.FieldValues);
            var originalValues = ParseFieldValues(requestData.OriginalValues);
            var requestType = (RequestType)requestData.RequestType;

            switch (requestType)
            {
                case RequestType.Insert:
                    return await _dataService.InsertDataAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema,
                        fieldValues);

                case RequestType.Update:
                    var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema);

                    var whereConditions = new Dictionary<string, object?>();
                    foreach (var pk in primaryKeyColumns)
                    {
                        if (originalValues.ContainsKey(pk))
                            whereConditions[pk] = originalValues[pk];
                    }

                    return await _dataService.UpdateDataAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema,
                        fieldValues,
                        whereConditions);

                case RequestType.Delete:
                    var deletePkColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema);

                    var deleteConditions = new Dictionary<string, object?>();
                    foreach (var pk in deletePkColumns)
                    {
                        if (originalValues.ContainsKey(pk))
                            deleteConditions[pk] = originalValues[pk];
                    }

                    return await _dataService.DeleteDataAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema,
                        deleteConditions);

                default:
                    _logger.LogWarning("Unknown request type {RequestType}", requestType);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying form request data changes");
            return false;
        }
    }

    private async Task<bool> ApplyBulkRequestItemsAsync(SqlConnection connection, int bulkFormRequestId)
    {
        try
        {
            _logger.LogInformation("Applying bulk request items for bulk request {BulkRequestId}", bulkFormRequestId);

            // Get bulk request and form definition details
            const string getBulkRequestSql = @"
                SELECT bfr.RequestType, fd.DatabaseConnectionName, fd.TableName, fd.[Schema]
                FROM BulkFormRequests bfr
                INNER JOIN FormDefinitions fd ON bfr.FormDefinitionId = fd.Id
                WHERE bfr.Id = @BulkRequestId";

            var bulkRequestData = await connection.QuerySingleOrDefaultAsync(getBulkRequestSql, new { BulkRequestId = bulkFormRequestId });

            if (bulkRequestData == null)
            {
                _logger.LogWarning("Bulk request {BulkRequestId} not found", bulkFormRequestId);
                return false;
            }

            // Extract typed values from dynamic to avoid RuntimeBinderException with LINQ extension methods
            var requestType = (RequestType)(int)bulkRequestData.RequestType;
            string dbConnectionName = (string)bulkRequestData.DatabaseConnectionName;
            string tableName = (string)bulkRequestData.TableName;
            string schema = (string)bulkRequestData.Schema;

            // Get all approved bulk request items
            const string getItemsSql = @"
                SELECT Id, FieldValues, OriginalValues, RowNumber
                FROM BulkFormRequestItems
                WHERE BulkFormRequestId = @BulkRequestId AND Status = @ApprovedStatus
                ORDER BY RowNumber";

            var items = await connection.QueryAsync(getItemsSql, new
            {
                BulkRequestId = bulkFormRequestId,
                ApprovedStatus = (int)RequestStatus.Approved
            });

            if (!items.Any())
            {
                _logger.LogInformation("No approved items found for bulk request {BulkRequestId}", bulkFormRequestId);
                return true;
            }

            _logger.LogInformation("Found {ItemCount} approved items to process for bulk request {BulkRequestId}", items.Count(), bulkFormRequestId);

            int successCount = 0;
            int failureCount = 0;

            foreach (var item in items)
            {
                try
                {
                    var fieldValuesJson = item.FieldValues?.ToString() ?? "{}";
                    var originalValuesJson = item.OriginalValues?.ToString() ?? "{}";

                    var fieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldValuesJson) ?? new Dictionary<string, object?>();
                    var originalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(originalValuesJson) ?? new Dictionary<string, object?>();

                    fieldValues = ConvertJsonElements(fieldValues);
                    originalValues = ConvertJsonElements(originalValues);

                    bool itemSuccess = false;
                    string processingResult = "";

                    switch (requestType)
                    {
                        case RequestType.Insert:
                            itemSuccess = await _dataService.InsertDataAsync(
                                dbConnectionName,
                                tableName,
                                schema,
                                fieldValues);
                            processingResult = itemSuccess ? "Successfully inserted into database" : "Failed to insert into database";
                            break;

                        case RequestType.Update:
                            var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                                dbConnectionName,
                                tableName,
                                schema);

                            if (!primaryKeyColumns.Any())
                                throw new InvalidOperationException($"No primary key found for table {schema}.{tableName}");

                            var whereConditions = new Dictionary<string, object?>();
                            foreach (var pkColumn in primaryKeyColumns)
                            {
                                if (originalValues.ContainsKey(pkColumn))
                                    whereConditions[pkColumn] = originalValues[pkColumn];
                                else
                                    throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values for item {item.Id}");
                            }

                            itemSuccess = await _dataService.UpdateDataAsync(
                                dbConnectionName,
                                tableName,
                                schema,
                                fieldValues,
                                whereConditions);
                            processingResult = itemSuccess ? "Successfully updated in database" : "Failed to update in database";
                            break;

                        case RequestType.Delete:
                            var deletePkColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                                dbConnectionName,
                                tableName,
                                schema);

                            if (!deletePkColumns.Any())
                                throw new InvalidOperationException($"No primary key found for table {schema}.{tableName}");

                            var deleteConditions = new Dictionary<string, object?>();
                            foreach (var pkColumn in deletePkColumns)
                            {
                                if (originalValues.ContainsKey(pkColumn))
                                    deleteConditions[pkColumn] = originalValues[pkColumn];
                                else
                                    throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values for item {item.Id}");
                            }

                            itemSuccess = await _dataService.DeleteDataAsync(
                                dbConnectionName,
                                tableName,
                                schema,
                                deleteConditions);
                            processingResult = itemSuccess ? "Successfully deleted from database" : "Failed to delete from database";
                            break;
                    }

                    // Update item status
                    const string updateItemSql = @"
                        UPDATE BulkFormRequestItems
                        SET Status = @Status, ProcessingResult = @ProcessingResult
                        WHERE Id = @ItemId";

                    await connection.ExecuteAsync(updateItemSql, new
                    {
                        ItemId = item.Id,
                        Status = itemSuccess ? (int)RequestStatus.Applied : (int)RequestStatus.Failed,
                        ProcessingResult = processingResult
                    });

                    if (itemSuccess)
                    {
                        successCount++;
                        _logger.LogInformation("Successfully applied bulk request item {ItemId} (row {RowNumber}) to database", (object)item.Id, (object)item.RowNumber);
                    }
                    else
                    {
                        failureCount++;
                        _logger.LogWarning("Failed to apply bulk request item {ItemId} (row {RowNumber}) to database", (object)item.Id, (object)item.RowNumber);
                    }
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logger.LogError(ex, "Error processing bulk request item {ItemId} (row {RowNumber})", (object)item.Id, (object)item.RowNumber);

                    const string updateFailedItemSql = @"
                        UPDATE BulkFormRequestItems
                        SET Status = @Status, ProcessingResult = @ProcessingResult
                        WHERE Id = @ItemId";

                    await connection.ExecuteAsync(updateFailedItemSql, new
                    {
                        ItemId = item.Id,
                        Status = (int)RequestStatus.Failed,
                        ProcessingResult = $"Error: {ex.Message}"
                    });
                }
            }

            // Update bulk request status
            var overallStatus = failureCount == 0 ? RequestStatus.Applied :
                              successCount > 0 ? RequestStatus.Applied : RequestStatus.Failed;

            var processingSummary = $"Processed {successCount + failureCount} items. {successCount} succeeded, {failureCount} failed.";

            const string updateBulkRequestSql = @"
                UPDATE BulkFormRequests
                SET Status = @Status, ProcessingSummary = @ProcessingSummary
                WHERE Id = @BulkRequestId";

            await connection.ExecuteAsync(updateBulkRequestSql, new
            {
                BulkRequestId = bulkFormRequestId,
                Status = (int)overallStatus,
                ProcessingSummary = processingSummary
            });

            _logger.LogInformation("Completed bulk request processing for {BulkRequestId}. Summary: {ProcessingSummary}",
                bulkFormRequestId, processingSummary);

            return successCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying bulk request items for bulk request {BulkRequestId}", bulkFormRequestId);
            return false;
        }
    }

    private Dictionary<string, object?> ParseFieldValues(object? fieldValuesObj)
    {
        if (fieldValuesObj == null) return new Dictionary<string, object?>();

        var json = fieldValuesObj.ToString();
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, object?>();

        try
        {
            var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new Dictionary<string, object?>();
            return ConvertJsonElements(result);
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private Dictionary<string, object?> ConvertJsonElements(Dictionary<string, object?> dict)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kvp in dict)
        {
            if (kvp.Value is JsonElement element)
            {
                result[kvp.Key] = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => element.ToString()
                };
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    private async Task SendStepActionNotificationAsync(int formRequestId, string stepId, string stepName, WorkflowStepAction action, string userId, string userName, string? comments)
    {
        // Simplified - only send rejection notifications to requestor
        if (action != WorkflowStepAction.Rejected) return;

        try
        {
            using var connection = (SqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();

            const string sql = @"
                SELECT fr.*, fd.Name as FormName,
                       COALESCE(u.Email, fr.RequestedBy) as RequestorEmail,
                       COALESCE(u.DisplayName, fr.RequestedBy) as RequestedByName
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                LEFT JOIN Users u ON u.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
                WHERE fr.Id = @FormRequestId";

            var data = await connection.QueryFirstOrDefaultAsync(sql, new { FormRequestId = formRequestId });
            if (data == null || string.IsNullOrEmpty(data.RequestorEmail?.ToString())) return;

            var baseUrl = _configuration["AppBranding:BaseUrl"] ?? "http://localhost:8080";
            var variables = new Dictionary<string, string>
            {
                { "{{RequestId}}", formRequestId.ToString() },
                { "{{FormName}}", data.FormName?.ToString() ?? "Unknown Form" },
                { "{{WorkflowStepName}}", stepName },
                { "{{Action}}", action.ToString() },
                { "{{ApproverName}}", userName },
                { "{{ApproverComments}}", comments ?? "" },
                { "{{RequestUrl}}", $"{baseUrl}/requests/{formRequestId}" },
                { "{{SystemName}}", "Requestr" }
            };

            await _notificationService.SendNotificationAsync(RequestRejected, variables, data.RequestorEmail.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending step action notification");
        }
    }

    private async Task SendWorkflowCompletedNotificationAsync(int formRequestId, string completedBy, bool wasApproved, string? comments)
    {
        try
        {
            using var connection = (SqlConnection)_connectionFactory.CreateConnection();
            await connection.OpenAsync();

            const string sql = @"
                SELECT fr.*, fd.Name as FormName,
                       COALESCE(u.Email, fr.RequestedBy) as RequestorEmail,
                       COALESCE(u.DisplayName, fr.RequestedBy) as RequestedByName
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                LEFT JOIN Users u ON u.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
                WHERE fr.Id = @FormRequestId";

            var data = await connection.QueryFirstOrDefaultAsync(sql, new { FormRequestId = formRequestId });
            if (data == null || string.IsNullOrEmpty(data.RequestorEmail?.ToString())) return;

            var baseUrl = _configuration["AppBranding:BaseUrl"] ?? "http://localhost:8080";
            var templateKey = wasApproved ? RequestApproved : RequestRejected;

            var variables = new Dictionary<string, string>
            {
                { "{{RequestId}}", formRequestId.ToString() },
                { "{{FormName}}", data.FormName?.ToString() ?? "Unknown Form" },
                { "{{ApproverName}}", completedBy },
                { "{{ApproverComments}}", comments ?? "" },
                { "{{RequestUrl}}", $"{baseUrl}/requests/{formRequestId}" },
                { "{{SystemName}}", "Requestr" }
            };

            await _notificationService.SendNotificationAsync(templateKey, variables, data.RequestorEmail.ToString());
            _logger.LogInformation("Sent workflow {Status} notification for request {RequestId}",
                wasApproved ? "approved" : "rejected", formRequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending workflow completed notification for request {RequestId}", formRequestId);
        }
    }

    private async Task SendStepBecomesActiveNotificationAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string stepId)
    {
        try
        {
            const string sql = @"
                SELECT 
                    wi.Id as WorkflowInstanceId,
                    wi.FormRequestId,
                    fr.BulkFormRequestId,
                    fd.Name as FormName,
                    wd.Name as WorkflowName,
                    fr.RequestType,
                    COALESCE(bfr.Comments, fr.Comments) as RequestComments,
                    fr.RequestedAt,
                    COALESCE(uReq.Email, fr.RequestedBy) as RequestorEmail,
                    COALESCE(uReq.DisplayName, fr.RequestedBy) as RequestedByName,
                    ws.Name as StepName,
                    ws.Description as StepDescription,
                    ws.NotificationEmail as StepNotificationEmail
                FROM WorkflowInstances wi
                INNER JOIN FormRequests fr ON wi.FormRequestId = fr.Id
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                INNER JOIN WorkflowDefinitions wd ON wi.WorkflowDefinitionId = wd.Id
                INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wd.Id AND ws.StepId = @StepId
                LEFT JOIN BulkFormRequests bfr ON fr.BulkFormRequestId = bfr.Id
                LEFT JOIN Users uReq ON uReq.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
                WHERE wi.Id = @WorkflowInstanceId";

            var stepData = await connection.QueryFirstOrDefaultAsync(sql, new
            {
                WorkflowInstanceId = workflowInstanceId,
                StepId = stepId
            }, transaction);

            if (stepData == null || string.IsNullOrWhiteSpace((string?)stepData.StepNotificationEmail))
            {
                _logger.LogDebug("No notification email configured for step {StepId}", stepId);
                return;
            }

            var baseUrl = _configuration["AppBranding:BaseUrl"] ?? "http://localhost:8080";
            int? bulkFormRequestId = stepData.BulkFormRequestId as int?;
            string requestUrl = bulkFormRequestId.HasValue
                ? $"{baseUrl}/bulk-requests/{bulkFormRequestId.Value}"
                : $"{baseUrl}/workflow/{workflowInstanceId}/step/{stepId}";

            var variables = new Dictionary<string, string>
            {
                { "{{RequestId}}", bulkFormRequestId?.ToString() ?? stepData.FormRequestId.ToString() },
                { "{{FormName}}", (stepData.FormName ?? "Form Request").ToString() },
                { "{{WorkflowName}}", (stepData.WorkflowName ?? "Workflow").ToString() },
                { "{{WorkflowStepName}}", (stepData.StepName ?? stepId).ToString() },
                { "{{RequestDescription}}", (stepData.RequestType?.ToString() ?? "Request") },
                { "{{RequestComments}}", (stepData.RequestComments ?? "").ToString() },
                { "{{CreatingUser}}", (stepData.RequestedByName ?? "Unknown").ToString() },
                { "{{CreatingUserEmail}}", (stepData.RequestorEmail ?? "").ToString() },
                { "{{RequestCreatedDate}}", stepData.RequestedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                { "{{RequestUrl}}", requestUrl },
                { "{{SystemName}}", "Requestr" },
                { "{{AssignedUserName}}", "" },
                { "{{AssignedUserEmail}}", "" },
                { "{{DueDate}}", "" }
            };

            await _notificationService.SendNotificationAsync(WorkflowStepPending, variables, (string)stepData.StepNotificationEmail);
            _logger.LogInformation("Sent step pending notification for workflow {InstanceId}, step {StepId} to {Email}",
                workflowInstanceId, stepId, (string)stepData.StepNotificationEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending step active notification for workflow {WorkflowInstanceId}, step {StepId}",
                workflowInstanceId, stepId);
        }
    }
}
