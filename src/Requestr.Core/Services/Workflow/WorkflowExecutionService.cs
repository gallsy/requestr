using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using Requestr.Core.Utilities;

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
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly INotificationService _notificationService;
    private readonly IWebhookExecutionService _webhookExecutionService;
    private readonly IFormRequestRepository _formRequestRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkflowExecutionService> _logger;

    private const string RequestApproved = "REQUEST_APPROVED";
    private const string RequestRejected = "REQUEST_REJECTED";
    private const string WorkflowStepPending = "WORKFLOW_STEP_PENDING";

    /// <summary>
    /// Captures actions that must run after the transaction has been committed,
    /// to avoid self-deadlocking when new connections try to read rows locked by the open transaction.
    /// </summary>
    private record PostCommitActions(
        int? FormRequestIdForNotification = null,
        string? CompletedBy = null,
        bool WasApproved = false,
        string? Comments = null,
        int? WorkflowInstanceIdForDataApplication = null,
        WebhookPostCommitInfo? WebhookInfo = null);

    /// <summary>
    /// Information needed to execute a webhook step after transaction commit.
    /// </summary>
    private record WebhookPostCommitInfo(
        int WorkflowInstanceId,
        string StepId,
        WebhookStepConfiguration Config,
        int FormRequestId);

    public WorkflowExecutionService(
        IWorkflowInstanceRepository instanceRepository,
        IWorkflowStepInstanceRepository stepInstanceRepository,
        IWorkflowDefinitionRepository definitionRepository,
        IDbConnectionFactory connectionFactory,
        IDataService dataService,
        IFormDefinitionService formDefinitionService,
        INotificationService notificationService,
        IWebhookExecutionService webhookExecutionService,
        IFormRequestRepository formRequestRepository,
        IConfiguration configuration,
        ILogger<WorkflowExecutionService> logger)
    {
        _instanceRepository = instanceRepository;
        _stepInstanceRepository = stepInstanceRepository;
        _definitionRepository = definitionRepository;
        _connectionFactory = connectionFactory;
        _dataService = dataService;
        _formDefinitionService = formDefinitionService;
        _notificationService = notificationService;
        _webhookExecutionService = webhookExecutionService;
        _formRequestRepository = formRequestRepository;
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

                // Verify this is one of the current steps
                if (!instance.IsCurrentStep(stepId))
                {
                    _logger.LogWarning("Step {StepId} is not a current step ({CurrentStepId}) for workflow {InstanceId}",
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
            PostCommitActions? postCommitActions = null;
            if (action == WorkflowStepAction.Rejected)
            {
                postCommitActions = await RejectWorkflowAsync((SqlConnection)connection, (SqlTransaction)transaction, workflowInstanceId, completedBy, comments, instance.FormRequestId);
            }
            else
            {
                // Get all next step IDs (fan-out: multiple outgoing transitions)
                var nextStepIds = GetNextStepIds(definition, stepId);

                // Remove the completed step from current steps
                var currentStepIds = instance.GetCurrentStepIds();
                currentStepIds.RemoveAll(id => id.Equals(stepId, StringComparison.OrdinalIgnoreCase));

                if (nextStepIds.Count > 0)
                {
                    postCommitActions = await MoveToNextStepsAsync(
                        (SqlConnection)connection, (SqlTransaction)transaction,
                        workflowInstanceId, nextStepIds, currentStepIds, definition, completedBy);
                }
                else if (currentStepIds.Count > 0)
                {
                    // No outgoing transitions from this step, but other parallel steps still active
                    instance.SetCurrentStepIds(currentStepIds);
                    await _instanceRepository.UpdateCurrentStepAsync(
                        workflowInstanceId, instance.CurrentStepId, connection, transaction);
                }
            }

            await transaction.CommitAsync();

            // Send notification after commit
            try
            {
                await SendStepActionNotificationAsync(instance.FormRequestId, stepId, 
                    definition.Steps.FirstOrDefault(s => s.StepId == stepId)?.Name ?? stepId, 
                    action, completedBy, completedByName, comments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending step action notification for step {StepId}", stepId);
            }

            // Execute deferred post-commit actions (notification + data application)
            // These open their own connections and would deadlock if run inside the transaction.
            if (postCommitActions != null)
            {
                await ExecutePostCommitActionsAsync(postCommitActions);
            }

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
                Message = "An unexpected error occurred while processing the workflow action."
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
    public async Task<List<WorkflowStepInstance>> GetCurrentWorkflowStepsAsync(int workflowInstanceId)
    {
        return await _stepInstanceRepository.GetCurrentStepsAsync(workflowInstanceId);
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

    /// <inheritdoc />
    public async Task ProcessPendingWebhookStepAsync(int workflowInstanceId)
    {
        try
        {
            var instance = await _instanceRepository.GetByIdAsync(workflowInstanceId);
            if (instance == null || instance.Status != WorkflowInstanceStatus.InProgress)
                return;

            var definition = await _definitionRepository.GetByIdAsync(instance.WorkflowDefinitionId);
            if (definition == null)
                return;

            // Check all current steps for webhook steps (supports parallel execution)
            foreach (var currentStepId in instance.GetCurrentStepIds())
            {
                var currentStep = definition.Steps.FirstOrDefault(s => s.StepId == currentStepId);
                if (currentStep?.StepType != WorkflowStepType.Webhook)
                    continue;

                var webhookConfig = currentStep.Configuration?.Webhook;
                if (webhookConfig == null)
                {
                    _logger.LogWarning("Webhook step {StepId} has no configuration — auto-completing", currentStep.StepId);
                    await CompleteStepAsync(workflowInstanceId, currentStep.StepId, "System", "System",
                        WorkflowStepAction.Completed, "Webhook skipped: no configuration", null);
                    return;
                }

                var webhookInfo = new WebhookPostCommitInfo(
                    WorkflowInstanceId: workflowInstanceId,
                    StepId: currentStep.StepId,
                    Config: webhookConfig,
                    FormRequestId: instance.FormRequestId);

                await ExecuteWebhookPostCommitAsync(webhookInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pending webhook step for workflow {WorkflowInstanceId}", workflowInstanceId);
        }
    }

    /// <summary>
    /// Gets all target step IDs from outgoing transitions (supports fan-out for parallel execution).
    /// </summary>
    private List<string> GetNextStepIds(WorkflowDefinition definition, string currentStepId)
    {
        var transitions = definition.Transitions
            .Where(t => t.FromStepId.Equals(currentStepId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!transitions.Any())
        {
            _logger.LogDebug("No transitions found from step {StepId}", currentStepId);
            return new List<string>();
        }

        return transitions.Select(t => t.ToStepId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Checks whether all incoming transitions to a step have been completed (for Parallel join steps).
    /// </summary>
    private bool AreAllIncomingStepsCompleted(
        WorkflowDefinition definition,
        string targetStepId,
        int workflowInstanceId,
        List<WorkflowStepInstance> stepInstances)
    {
        // Find all steps that have a transition leading into the target
        var incomingStepIds = definition.Transitions
            .Where(t => t.ToStepId.Equals(targetStepId, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FromStepId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Check that all incoming steps have been completed
        foreach (var incomingStepId in incomingStepIds)
        {
            var stepInstance = stepInstances.FirstOrDefault(
                si => si.StepId.Equals(incomingStepId, StringComparison.OrdinalIgnoreCase));
            if (stepInstance == null || stepInstance.Status != WorkflowStepInstanceStatus.Completed)
            {
                _logger.LogDebug("Parallel join step {TargetStepId} waiting for step {IncomingStepId} to complete",
                    targetStepId, incomingStepId);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Moves the workflow to one or more next steps, handling parallel fan-out and join semantics.
    /// For Parallel steps (join), waits until all incoming transitions are complete before advancing.
    /// </summary>
    private async Task<PostCommitActions?> MoveToNextStepsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int workflowInstanceId,
        List<string> nextStepIds,
        List<string> remainingCurrentStepIds,
        WorkflowDefinition definition,
        string completedBy)
    {
        // Load all step instances for join checks
        var allStepInstances = await _stepInstanceRepository.GetByWorkflowInstanceIdAsync(workflowInstanceId, connection, transaction);
        var activatedStepIds = new List<string>(remainingCurrentStepIds);
        PostCommitActions? postCommitActions = null;

        foreach (var nextStepId in nextStepIds)
        {
            var nextStep = definition.Steps.FirstOrDefault(s => s.StepId.Equals(nextStepId, StringComparison.OrdinalIgnoreCase));
            if (nextStep == null) continue;

            // For Parallel (join) steps: check all incoming transitions are complete
            if (nextStep.StepType == WorkflowStepType.Parallel)
            {
                if (!AreAllIncomingStepsCompleted(definition, nextStepId, workflowInstanceId, allStepInstances))
                {
                    _logger.LogInformation("Parallel join step {StepId} not yet ready — waiting for other branches", nextStepId);
                    continue;
                }
            }

            // Activate this step
            await _stepInstanceRepository.UpdateToInProgressAsync(workflowInstanceId, nextStepId, connection, transaction);
            activatedStepIds.Add(nextStepId);

            // Handle auto-completing step types
            if (nextStep.StepType == WorkflowStepType.End)
            {
                // Remove from active — it will be completed
                activatedStepIds.RemoveAll(id => id.Equals(nextStepId, StringComparison.OrdinalIgnoreCase));
                postCommitActions = await AutoCompleteEndStepAsync(connection, transaction, workflowInstanceId, nextStepId, completedBy);
            }
            else if (nextStep.StepType == WorkflowStepType.Parallel)
            {
                // Parallel (join) step auto-completes and fans out to its successors
                await _stepInstanceRepository.UpdateToCompletedAsync(
                    workflowInstanceId, nextStepId, "System", WorkflowStepAction.Completed,
                    "Auto-completed Parallel join step", null, connection, transaction);

                activatedStepIds.RemoveAll(id => id.Equals(nextStepId, StringComparison.OrdinalIgnoreCase));

                // Recursively advance to the Parallel step's successors
                var parallelNextStepIds = GetNextStepIds(definition, nextStepId);
                if (parallelNextStepIds.Count > 0)
                {
                    // Refresh step instances after completing the parallel step
                    allStepInstances = await _stepInstanceRepository.GetByWorkflowInstanceIdAsync(workflowInstanceId, connection, transaction);
                    postCommitActions = await MoveToNextStepsAsync(
                        connection, transaction, workflowInstanceId,
                        parallelNextStepIds, activatedStepIds, definition, completedBy);
                    return postCommitActions; // activatedStepIds is updated by the recursive call
                }
            }
            else if (nextStep.StepType == WorkflowStepType.Webhook)
            {
                var webhookConfig = nextStep.Configuration?.Webhook;
                if (webhookConfig == null)
                {
                    _logger.LogWarning("Webhook step {StepId} has no webhook configuration — auto-completing", nextStepId);
                    await _stepInstanceRepository.UpdateToCompletedAsync(
                        workflowInstanceId, nextStepId, "System", WorkflowStepAction.Completed,
                        "Auto-completed webhook step (no configuration)", null, connection, transaction);

                    activatedStepIds.RemoveAll(id => id.Equals(nextStepId, StringComparison.OrdinalIgnoreCase));

                    var afterWebhookStepIds = GetNextStepIds(definition, nextStepId);
                    if (afterWebhookStepIds.Count > 0)
                    {
                        allStepInstances = await _stepInstanceRepository.GetByWorkflowInstanceIdAsync(workflowInstanceId, connection, transaction);
                        postCommitActions = await MoveToNextStepsAsync(
                            connection, transaction, workflowInstanceId,
                            afterWebhookStepIds, activatedStepIds, definition, completedBy);
                        return postCommitActions;
                    }
                }
                else
                {
                    // Webhook stays InProgress — will execute post-commit
                    var instance = await _instanceRepository.GetByIdAsync(workflowInstanceId, connection, transaction);
                    postCommitActions = new PostCommitActions(
                        WebhookInfo: new WebhookPostCommitInfo(
                            WorkflowInstanceId: workflowInstanceId,
                            StepId: nextStepId,
                            Config: webhookConfig,
                            FormRequestId: instance?.FormRequestId ?? 0));
                }
            }
            else
            {
                // Approval or other interactive step — send notification
                try
                {
                    await SendStepBecomesActiveNotificationAsync(connection, transaction, workflowInstanceId, nextStepId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending step active notification for workflow {InstanceId}, step {StepId}",
                        workflowInstanceId, nextStepId);
                }
            }
        }

        // Update the current step IDs on the workflow instance
        var newCurrentStepIds = activatedStepIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var currentStepIdValue = string.Join(",", newCurrentStepIds);
        await _instanceRepository.UpdateCurrentStepAsync(workflowInstanceId, currentStepIdValue, connection, transaction);

        _logger.LogDebug("Moved workflow {InstanceId} to steps [{StepIds}]",
            workflowInstanceId, currentStepIdValue);

        return postCommitActions;
    }

    private async Task<PostCommitActions?> AutoCompleteEndStepAsync(
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

        // Cancel any remaining active step instances (parallel branches that didn't finish first)
        await CancelRemainingStepsAsync(connection, transaction, workflowInstanceId);

        // Get the form request ID and approve/apply the form request
        var instance = await _instanceRepository.GetByIdAsync(workflowInstanceId, connection, transaction);
        if (instance != null)
        {
            await ApproveFormRequestAsync(connection, transaction, instance.FormRequestId, completedBy);
        }

        _logger.LogInformation("Workflow {InstanceId} completed at End step {StepId}", workflowInstanceId, stepId);

        // Return deferred actions — these open their own connections and must run AFTER commit
        if (instance != null)
        {
            return new PostCommitActions(
                FormRequestIdForNotification: instance.FormRequestId,
                CompletedBy: completedBy,
                WasApproved: true,
                WorkflowInstanceIdForDataApplication: workflowInstanceId);
        }

        return null;
    }

    private async Task<PostCommitActions> RejectWorkflowAsync(
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

        // Cancel any remaining active step instances (parallel branches)
        await CancelRemainingStepsAsync(connection, transaction, workflowInstanceId);

        _logger.LogInformation("Rejected workflow instance {WorkflowInstanceId} by user {UserId}", workflowInstanceId, rejectedBy);

        // Return deferred action — notification must run AFTER commit to avoid deadlock
        return new PostCommitActions(
            FormRequestIdForNotification: formRequestId,
            CompletedBy: rejectedBy,
            WasApproved: false,
            Comments: rejectionReason);
    }

    /// <summary>
    /// Executes notification and data application actions that were deferred to run
    /// after the workflow transaction has been committed, avoiding self-deadlock.
    /// </summary>
    private async Task ExecutePostCommitActionsAsync(PostCommitActions actions)
    {
        if (actions.FormRequestIdForNotification.HasValue && actions.CompletedBy != null)
        {
            try
            {
                await SendWorkflowCompletedNotificationAsync(
                    actions.FormRequestIdForNotification.Value,
                    actions.CompletedBy,
                    actions.WasApproved,
                    actions.Comments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending workflow completed notification for request {FormRequestId}",
                    actions.FormRequestIdForNotification.Value);
            }
        }

        if (actions.WorkflowInstanceIdForDataApplication.HasValue && actions.CompletedBy != null)
        {
            try
            {
                await ApplyWorkflowDataChangesAsync(
                    actions.WorkflowInstanceIdForDataApplication.Value,
                    actions.CompletedBy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying data changes for workflow {WorkflowInstanceId}",
                    actions.WorkflowInstanceIdForDataApplication.Value);
            }
        }

        if (actions.WebhookInfo != null)
        {
            await ExecuteWebhookPostCommitAsync(actions.WebhookInfo);
        }
    }

    /// <summary>
    /// Executes a webhook step after transaction commit, then auto-completes the step
    /// and advances the workflow. This avoids holding DB transactions open during HTTP calls.
    /// </summary>
    private async Task ExecuteWebhookPostCommitAsync(WebhookPostCommitInfo info)
    {
        try
        {
            _logger.LogInformation("Executing webhook for workflow {WorkflowInstanceId}, step {StepId}",
                info.WorkflowInstanceId, info.StepId);

            // Load the form request for variable substitution
            var formRequest = await _formRequestRepository.GetByIdAsync(info.FormRequestId);
            if (formRequest == null)
            {
                _logger.LogError("Form request {FormRequestId} not found for webhook step", info.FormRequestId);
                await CompleteStepAsync(info.WorkflowInstanceId, info.StepId, "System", "System",
                    WorkflowStepAction.Completed, "Webhook skipped: form request not found", null);
                return;
            }

            // Load form definition for system variables
            FormDefinition? formDefinition = null;
            try
            {
                formDefinition = await _formDefinitionService.GetByIdAsync(formRequest.FormDefinitionId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load form definition {FormDefId} for webhook variables",
                    formRequest.FormDefinitionId);
            }

            // Execute the webhook
            var result = await _webhookExecutionService.ExecuteAsync(info.Config, formRequest, formDefinition);

            // Build completion comment with webhook result summary
            var comment = result.Success
                ? $"Webhook completed: HTTP {result.StatusCode}"
                : $"Webhook failed: {result.ErrorMessage}";

            if (result.Success)
            {
                // Complete the step and advance the workflow
                await CompleteStepAsync(info.WorkflowInstanceId, info.StepId, "System", "System",
                    WorkflowStepAction.Completed, comment, null);
            }
            else
            {
                // Fail the step — this will reject/fail the workflow
                await CompleteStepAsync(info.WorkflowInstanceId, info.StepId, "System", "System",
                    WorkflowStepAction.Rejected, comment, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing webhook for workflow {WorkflowInstanceId}, step {StepId}",
                info.WorkflowInstanceId, info.StepId);

            // Attempt to fail the step gracefully
            try
            {
                await CompleteStepAsync(info.WorkflowInstanceId, info.StepId, "System", "System",
                    WorkflowStepAction.Rejected, $"Webhook execution error: {ex.Message}", null);
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to mark webhook step as failed for workflow {WorkflowInstanceId}",
                    info.WorkflowInstanceId);
            }
        }
    }

    /// <summary>
    /// Cancels all remaining Pending or InProgress step instances for a workflow.
    /// Called when the workflow completes or is rejected so parallel branches don't dangle.
    /// </summary>
    private async Task CancelRemainingStepsAsync(
        SqlConnection connection, SqlTransaction transaction, int workflowInstanceId)
    {
        const string sql = @"
            UPDATE WorkflowStepInstances
            SET Status = @SkippedStatus,
                CompletedAt = @Now,
                CompletedBy = 'System',
                Action = @SkippedAction,
                Comments = 'Auto-skipped: workflow completed via another branch'
            WHERE WorkflowInstanceId = @WorkflowInstanceId
              AND Status IN (@PendingStatus, @InProgressStatus)";

        var affected = await connection.ExecuteAsync(sql, new
        {
            WorkflowInstanceId = workflowInstanceId,
            SkippedStatus = (int)WorkflowStepInstanceStatus.Skipped,
            SkippedAction = (int)WorkflowStepAction.None,
            PendingStatus = (int)WorkflowStepInstanceStatus.Pending,
            InProgressStatus = (int)WorkflowStepInstanceStatus.InProgress,
            Now = DateTime.UtcNow
        }, transaction);

        if (affected > 0)
        {
            _logger.LogInformation("Cancelled {Count} remaining step instances for completed workflow {WorkflowInstanceId}",
                affected, workflowInstanceId);
        }
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
                       fr.FormDefinitionId, fr.RequestedBy,
                       COALESCE(uReq.DisplayName, fr.RequestedBy) as RequestedByName,
                       COALESCE(uComp.DisplayName, @CompletedBy) as CompletedByName,
                       fd.DatabaseConnectionName, fd.TableName, fd.[Schema]
                FROM WorkflowInstances wi
                INNER JOIN FormRequests fr ON wi.FormRequestId = fr.Id
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                LEFT JOIN Users uReq ON uReq.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
                LEFT JOIN Users uComp ON uComp.UserObjectId = TRY_CONVERT(uniqueidentifier, @CompletedBy)
                WHERE wi.Id = @WorkflowInstanceId";

            var workflowData = await connection.QueryFirstOrDefaultAsync(getFormRequestSql, new { WorkflowInstanceId = workflowInstanceId, CompletedBy = completedBy });

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

                // Record workflow-approved history for the bulk request
                string completedByName = workflowData.CompletedByName;
                const string insertBulkHistorySql = @"
                    INSERT INTO BulkFormRequestHistory (BulkFormRequestId, ChangeType, ChangedBy, ChangedByName, ChangedAt, Comments, Details)
                    VALUES (@BulkFormRequestId, @ChangeType, @ChangedBy, @ChangedByName, @ChangedAt, @Comments, @Details)";
                await connection.ExecuteAsync(insertBulkHistorySql, new
                {
                    BulkFormRequestId = bulkFormRequestId.Value,
                    ChangeType = (int)FormRequestChangeType.Approved,
                    ChangedBy = completedBy,
                    ChangedByName = completedByName,
                    ChangedAt = DateTime.UtcNow,
                    Comments = "Approved via workflow",
                    Details = (string?)null
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
            int formDefinitionId = (int)requestData.FormDefinitionId;
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formDefinitionId);
            var fields = formDefinition?.Fields ?? new List<FormField>();

            var fieldValues = ParseFieldValues(requestData.FieldValues, fields);
            var originalValues = ParseFieldValues(requestData.OriginalValues, fields);
            var requestType = (RequestType)requestData.RequestType;

            // Inject computed values (e.g. current datetime, user info, GUID)
            await InjectComputedValuesAsync(fieldValues, fields, requestType, 
                requestData.RequestedBy?.ToString(), requestData.RequestedByName?.ToString());

            bool result;
            switch (requestType)
            {
                case RequestType.Insert:
                    result = await _dataService.InsertDataAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema,
                        fieldValues);
                    break;

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

                    result = await _dataService.UpdateDataAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema,
                        fieldValues,
                        whereConditions);
                    break;

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

                    result = await _dataService.DeleteDataAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema,
                        deleteConditions);
                    break;

                default:
                    _logger.LogWarning("Unknown request type {RequestType}", requestType);
                    return false;
            }

            // Save computed values back to the FormRequest so they're visible in the UI
            if (result)
            {
                try
                {
                    int formRequestId = (int)requestData.FormRequestId;
                    using var conn = await _connectionFactory.CreateConnectionAsync();
                    var json = System.Text.Json.JsonSerializer.Serialize(fieldValues);
                    await conn.ExecuteAsync(
                        "UPDATE FormRequests SET FieldValues = @FieldValues WHERE Id = @Id",
                        new { Id = formRequestId, FieldValues = json });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save computed field values back to form request");
                }
            }

            return result;
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
                SELECT bfr.RequestType, bfr.FormDefinitionId, fd.DatabaseConnectionName, fd.TableName, fd.[Schema]
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
            int bulkFormDefinitionId = (int)bulkRequestData.FormDefinitionId;
            var bulkFormDefinition = await _formDefinitionService.GetFormDefinitionAsync(bulkFormDefinitionId);
            var bulkFields = bulkFormDefinition?.Fields ?? new List<FormField>();
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

                    fieldValues = SqlTypeConverter.ConvertDictionary(fieldValues, bulkFields);
                    originalValues = SqlTypeConverter.ConvertDictionary(originalValues, bulkFields);

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

                            if (primaryKeyColumns.Count == 0)
                                throw new InvalidOperationException($"No primary key found for table {schema}.{tableName}");

                            var whereConditions = new Dictionary<string, object?>();
                            foreach (var pkColumn in primaryKeyColumns)
                            {
                                if (originalValues.ContainsKey(pkColumn))
                                    whereConditions[pkColumn] = originalValues[pkColumn];
                                else
                                    throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values for item {(int)item.Id}");
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

                            if (deletePkColumns.Count == 0)
                                throw new InvalidOperationException($"No primary key found for table {schema}.{tableName}");

                            var deleteConditions = new Dictionary<string, object?>();
                            foreach (var pkColumn in deletePkColumns)
                            {
                                if (originalValues.ContainsKey(pkColumn))
                                    deleteConditions[pkColumn] = originalValues[pkColumn];
                                else
                                    throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values for item {(int)item.Id}");
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

            // Record history entry for bulk request
            var historyChangeType = failureCount == 0 ? FormRequestChangeType.Applied : FormRequestChangeType.Failed;
            const string insertHistorySql = @"
                INSERT INTO BulkFormRequestHistory (BulkFormRequestId, ChangeType, ChangedBy, ChangedByName, ChangedAt, Comments, Details)
                VALUES (@BulkFormRequestId, @ChangeType, 'system', 'System', @ChangedAt, @Comments, @Details)";
            await connection.ExecuteAsync(insertHistorySql, new
            {
                BulkFormRequestId = bulkFormRequestId,
                ChangeType = (int)historyChangeType,
                ChangedAt = DateTime.UtcNow,
                Comments = processingSummary,
                Details = (string?)null
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

    private Dictionary<string, object?> ParseFieldValues(object? fieldValuesObj, IReadOnlyList<FormField> fields)
    {
        if (fieldValuesObj == null) return new Dictionary<string, object?>();

        var json = fieldValuesObj.ToString();
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, object?>();

        try
        {
            var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new Dictionary<string, object?>();
            return SqlTypeConverter.ConvertDictionary(result, fields);
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
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

    /// <summary>
    /// Injects computed values into the field values dictionary based on field configuration.
    /// Values are resolved at apply-time (e.g. current datetime, user info, new GUID).
    /// </summary>
    private async Task InjectComputedValuesAsync(
        Dictionary<string, object?> fieldValues,
        List<FormField> fields,
        RequestType requestType,
        string? requestedBy,
        string? requestedByName)
    {
        foreach (var field in fields)
        {
            if (field.ComputedValueType == null || field.ComputedValueType == ComputedValueType.None)
                continue;

            var shouldApply = requestType switch
            {
                RequestType.Insert => field.ComputedValueApplyMode is ComputedValueApplyMode.InsertAndUpdate or ComputedValueApplyMode.InsertOnly,
                RequestType.Update => field.ComputedValueApplyMode is ComputedValueApplyMode.InsertAndUpdate or ComputedValueApplyMode.UpdateOnly,
                RequestType.Delete => false,
                _ => false
            };

            if (!shouldApply)
                continue;

            var computedValue = field.ComputedValueType switch
            {
                ComputedValueType.CurrentDateTimeUtc => (object)DateTime.UtcNow,
                ComputedValueType.CurrentDateTimeLocal => (object)DateTime.Now,
                ComputedValueType.CurrentUserId => requestedBy,
                ComputedValueType.CurrentUserDisplayName => requestedByName,
                ComputedValueType.CurrentUserEmail => await ResolveUserEmailAsync(requestedBy),
                ComputedValueType.NewGuid => Guid.NewGuid(),
                _ => null
            };

            if (computedValue != null)
            {
                fieldValues[field.Name] = computedValue;
            }
        }
    }

    private async Task<string?> ResolveUserEmailAsync(string? userObjectId)
    {
        if (string.IsNullOrEmpty(userObjectId) || !Guid.TryParse(userObjectId, out _))
            return userObjectId;

        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();
            var email = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT COALESCE(Email, UPN, @Fallback) FROM Users WHERE UserObjectId = TRY_CONVERT(uniqueidentifier, @UserObjectId)",
                new { UserObjectId = userObjectId, Fallback = userObjectId });
            return email ?? userObjectId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve email for user {UserObjectId}", userObjectId);
            return userObjectId;
        }
    }
}
