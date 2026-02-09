using Dapper;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using Requestr.Core.Services.Workflow;
using System.Text.Json;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Implementation of form request approval/rejection operations.
/// </summary>
public class FormRequestApprovalService : IFormRequestApprovalService
{
    private readonly IFormRequestRepository _formRequestRepository;
    private readonly IFormRequestHistoryService _historyService;
    private readonly IFormRequestApplicationService _applicationService;
    private readonly IWorkflowExecutionService _workflowExecutionService;
    private readonly IInputValidationService _inputValidationService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FormRequestApprovalService> _logger;

    public FormRequestApprovalService(
        IFormRequestRepository formRequestRepository,
        IFormRequestHistoryService historyService,
        IFormRequestApplicationService applicationService,
        IWorkflowExecutionService workflowExecutionService,
        IInputValidationService inputValidationService,
        IDbConnectionFactory connectionFactory,
        ILogger<FormRequestApprovalService> logger)
    {
        _formRequestRepository = formRequestRepository;
        _historyService = historyService;
        _applicationService = applicationService;
        _workflowExecutionService = workflowExecutionService;
        _inputValidationService = inputValidationService;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<bool> ApproveAsync(int id, string approvedBy, string approvedByName)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var formRequest = await _formRequestRepository.GetByIdAsync(id);
            if (formRequest == null || formRequest.Status != RequestStatus.Pending)
            {
                await transaction.RollbackAsync();
                return false;
            }

            // Update the request status to approved
            await _formRequestRepository.ApproveAsync(id, approvedBy, connection, transaction);

            // Record the approval in history
            await _historyService.RecordChangeAsync(
                id,
                FormRequestChangeType.Approved,
                new Dictionary<string, object?> { { "Status", "Pending" } },
                new Dictionary<string, object?> { { "Status", "Approved" } },
                approvedBy,
                approvedByName,
                "Request approved",
                connection,
                transaction
            );

            // Apply changes to target database
            try
            {
                var applicationResult = await _applicationService.ApplyChangesToDatabaseAsync(
                    formRequest, connection, transaction);

                if (applicationResult.Success)
                {
                    await _formRequestRepository.SetAppliedAsync(id, applicationResult.RecordKey, connection, transaction);

                    string successComment = formRequest.RequestType switch
                    {
                        RequestType.Insert => $"Record successfully inserted. New record key: {applicationResult.RecordKey}",
                        RequestType.Update => $"Record successfully updated. Updated record: {applicationResult.RecordKey}",
                        RequestType.Delete => $"Record successfully deleted. Deleted record: {applicationResult.RecordKey}",
                        _ => $"Request successfully applied. Record key: {applicationResult.RecordKey}"
                    };

                    await _historyService.RecordChangeAsync(
                        id,
                        FormRequestChangeType.Applied,
                        new Dictionary<string, object?> { { "Status", "Approved" } },
                        new Dictionary<string, object?> 
                        { 
                            { "Status", "Applied" },
                            { "AppliedRecordKey", applicationResult.RecordKey },
                            { "OperationType", formRequest.RequestType }
                        },
                        "System",
                        "System",
                        successComment,
                        connection,
                        transaction
                    );

                    _logger.LogInformation("Form request {Id} approved and applied. Record key: {RecordKey}",
                        id, applicationResult.RecordKey);
                }
                else
                {
                    throw new InvalidOperationException(applicationResult.ErrorMessage ?? "Failed to apply changes");
                }
            }
            catch (Exception applyEx)
            {
                _logger.LogError(applyEx, "Failed to apply form request {Id} after approval", id);

                await _formRequestRepository.SetFailedAsync(id, applyEx.Message, connection, transaction);

                await _historyService.RecordChangeAsync(
                    id,
                    FormRequestChangeType.Failed,
                    new Dictionary<string, object?> { { "Status", "Approved" } },
                    new Dictionary<string, object?> 
                    { 
                        { "Status", "Failed" },
                        { "FailureMessage", applyEx.Message }
                    },
                    "System",
                    "System",
                    $"Failed to apply request to target database: {applyEx.Message}",
                    connection,
                    transaction
                );
            }

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error approving form request {Id}", id);
            throw;
        }
    }

    public async Task<bool> RejectAsync(int id, string rejectedBy, string rejectedByName, string reason)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            var rowsAffected = await _formRequestRepository.RejectAsync(id, rejectedBy, reason, connection, null);

            if (rowsAffected > 0)
            {
                await _historyService.RecordChangeAsync(
                    id,
                    FormRequestChangeType.Rejected,
                    new Dictionary<string, object?> { { "Status", "Pending" } },
                    new Dictionary<string, object?> { { "Status", "Rejected" }, { "RejectionReason", reason } },
                    rejectedBy,
                    rejectedByName,
                    $"Request rejected: {reason}"
                );
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting form request {Id}", id);
            throw;
        }
    }

    public async Task<bool> RetryFailedAsync(int id, string retriedBy, string retriedByName)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            var formRequest = await _formRequestRepository.GetByIdAsync(id);
            if (formRequest == null || formRequest.Status != RequestStatus.Failed)
            {
                await transaction.RollbackAsync();
                return false;
            }

            // Reset status to Approved within the transaction
            const string updateStatusSql = @"
                UPDATE FormRequests 
                SET Status = @Status, FailureMessage = NULL 
                WHERE Id = @Id";
            await connection.ExecuteAsync(updateStatusSql, new { Id = id, Status = (int)RequestStatus.Approved }, transaction);

            await _historyService.RecordChangeAsync(
                id,
                FormRequestChangeType.StatusChanged,
                new Dictionary<string, object?> 
                { 
                    { "Status", "Failed" },
                    { "FailureMessage", formRequest.FailureMessage }
                },
                new Dictionary<string, object?> { { "Status", "Approved" } },
                retriedBy,
                retriedByName,
                "Request retry initiated",
                connection,
                transaction
            );

            // Try to apply again
            try
            {
                var applicationResult = await _applicationService.ApplyChangesToDatabaseAsync(
                    formRequest, connection, transaction);

                if (applicationResult.Success)
                {
                    await _formRequestRepository.SetAppliedAsync(id, applicationResult.RecordKey, connection, transaction);

                    string successComment = formRequest.RequestType switch
                    {
                        RequestType.Insert => $"Record successfully inserted after retry. New record key: {applicationResult.RecordKey}",
                        RequestType.Update => $"Record successfully updated after retry. Updated record: {applicationResult.RecordKey}",
                        RequestType.Delete => $"Record successfully deleted after retry. Deleted record: {applicationResult.RecordKey}",
                        _ => $"Request successfully applied after retry. Record key: {applicationResult.RecordKey}"
                    };

                    await _historyService.RecordChangeAsync(
                        id,
                        FormRequestChangeType.Applied,
                        new Dictionary<string, object?> { { "Status", "Approved" } },
                        new Dictionary<string, object?> 
                        { 
                            { "Status", "Applied" },
                            { "AppliedRecordKey", applicationResult.RecordKey },
                            { "OperationType", formRequest.RequestType },
                            { "RetryAttempt", true }
                        },
                        retriedBy,
                        retriedByName,
                        successComment,
                        connection,
                        transaction
                    );

                    _logger.LogInformation("Form request {Id} successfully retried and applied. Record key: {RecordKey}",
                        id, applicationResult.RecordKey);
                }
                else
                {
                    throw new InvalidOperationException(applicationResult.ErrorMessage ?? "Retry failed");
                }
            }
            catch (Exception applyEx)
            {
                _logger.LogError(applyEx, "Failed to apply form request {Id} during retry", id);

                await _formRequestRepository.SetFailedAsync(id, $"Retry attempt failed: {applyEx.Message}", connection, transaction);

                await _historyService.RecordChangeAsync(
                    id,
                    FormRequestChangeType.Failed,
                    new Dictionary<string, object?> { { "Status", "Approved" } },
                    new Dictionary<string, object?> 
                    { 
                        { "Status", "Failed" },
                        { "FailureMessage", $"Retry attempt failed: {applyEx.Message}" },
                        { "RetryAttempt", true }
                    },
                    retriedBy,
                    retriedByName,
                    $"Retry attempt failed: {applyEx.Message}",
                    connection,
                    transaction
                );

                await transaction.RollbackAsync();
                return false;
            }

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error retrying failed form request {Id}", id);
            throw;
        }
    }

    public async Task<bool> ProcessWorkflowActionAsync(
        int formRequestId, string actionType, string userId,
        string? comments = null, Dictionary<string, object?>? fieldUpdates = null)
    {
        try
        {
            var formRequest = await _formRequestRepository.GetByIdAsync(formRequestId);
            if (formRequest?.WorkflowInstanceId == null)
            {
                _logger.LogWarning("Form request {FormRequestId} does not have an active workflow", formRequestId);
                return false;
            }

            // Sanitize comments if provided
            var sanitizedComments = !string.IsNullOrEmpty(comments) 
                ? _inputValidationService.SanitizeComments(comments)
                : comments;

            var result = await _workflowExecutionService.ProcessWorkflowActionAsync(
                formRequest.WorkflowInstanceId.Value,
                actionType,
                userId,
                sanitizedComments,
                fieldUpdates);

            if (!result.Success)
            {
                _logger.LogWarning("Workflow action {ActionType} failed for form request {FormRequestId}: {Message}", 
                    actionType, formRequestId, result.Message);
                return false;
            }

            // Update form request status based on workflow status
            if (result.WorkflowCompleted)
            {
                formRequest.Status = result.WorkflowApproved ? RequestStatus.Approved : RequestStatus.Rejected;
            }

            // Apply any field updates
            if (fieldUpdates?.Any() == true)
            {
                foreach (var update in fieldUpdates)
                {
                    formRequest.FieldValues[update.Key] = update.Value;
                }
            }

            // Persist changes if status changed or field values updated
            if (result.WorkflowCompleted || fieldUpdates?.Any() == true)
            {
                await _formRequestRepository.UpdateAsync(formRequest);
            }

            // Record the action in form request history
            await _historyService.RecordChangeAsync(
                formRequestId,
                actionType.ToLower() switch
                {
                    "approve" => FormRequestChangeType.Approved,
                    "reject" => FormRequestChangeType.Rejected,
                    _ => FormRequestChangeType.StatusChanged
                },
                new Dictionary<string, object?> { { "PreviousStep", result.PreviousStepName } },
                new Dictionary<string, object?> 
                { 
                    { "CurrentStep", result.CurrentStepName },
                    { "ActionType", actionType },
                    { "Comments", sanitizedComments },
                    { "FieldUpdates", fieldUpdates }
                },
                userId,
                result.ActorName ?? userId,
                $"Workflow action: {actionType}" + (string.IsNullOrEmpty(sanitizedComments) ? "" : $" - {sanitizedComments}")
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing workflow action {ActionType} for form request {FormRequestId}", 
                actionType, formRequestId);
            throw;
        }
    }

    public async Task<WorkflowStepInstance?> GetCurrentWorkflowStepAsync(int formRequestId)
    {
        try
        {
            var formRequest = await _formRequestRepository.GetByIdAsync(formRequestId);
            if (formRequest?.WorkflowInstanceId == null)
            {
                return null;
            }

            return await _workflowExecutionService.GetCurrentWorkflowStepAsync(formRequest.WorkflowInstanceId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current workflow step for form request {FormRequestId}", formRequestId);
            throw;
        }
    }

    public async Task<List<WorkflowStepInstance>> GetCompletedWorkflowStepsAsync(int formRequestId)
    {
        try
        {
            var formRequest = await _formRequestRepository.GetByIdAsync(formRequestId);
            if (formRequest?.WorkflowInstanceId == null)
            {
                return new List<WorkflowStepInstance>();
            }

            return await _workflowExecutionService.GetCompletedWorkflowStepsAsync(formRequest.WorkflowInstanceId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting completed workflow steps for form request {FormRequestId}", formRequestId);
            throw;
        }
    }

    public async Task<int> ProcessStuckWorkflowRequestsAsync(string processedBy)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            var sql = @"
                SELECT fr.Id
                FROM FormRequests fr
                INNER JOIN WorkflowInstances wi ON fr.WorkflowInstanceId = wi.Id
                WHERE wi.Status = @WorkflowCompletedStatus
                  AND fr.Status = @ApprovedStatus";

            var stuckRequestIds = await connection.QueryAsync<int>(sql, new 
            { 
                WorkflowCompletedStatus = (int)WorkflowInstanceStatus.Completed,
                ApprovedStatus = (int)RequestStatus.Approved
            });

            int processedCount = 0;
            _logger.LogInformation("Found {Count} stuck workflow requests to process", stuckRequestIds.Count());

            foreach (var requestId in stuckRequestIds)
            {
                try
                {
                    var success = await _applicationService.ApplyAsync(requestId);
                    if (success)
                    {
                        processedCount++;
                        _logger.LogInformation("Successfully processed stuck workflow request {RequestId}", requestId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing stuck workflow request {RequestId}", requestId);
                }
            }

            return processedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stuck workflow requests");
            throw;
        }
    }
}
