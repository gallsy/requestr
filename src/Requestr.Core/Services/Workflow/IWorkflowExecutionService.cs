using Requestr.Core.Models;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service for executing workflow steps and processing actions.
/// Handles step completion, navigation, and workflow transitions.
/// </summary>
public interface IWorkflowExecutionService
{
    /// <summary>
    /// Completes a workflow step with the specified action.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="stepId">The step ID to complete.</param>
    /// <param name="completedBy">The user ID completing the step.</param>
    /// <param name="completedByName">The display name of the user.</param>
    /// <param name="action">The action taken (Approved, Rejected, etc.).</param>
    /// <param name="comments">Optional comments.</param>
    /// <param name="fieldValues">Field values collected during the step.</param>
    /// <returns>True if the step was completed successfully.</returns>
    Task<bool> CompleteStepAsync(
        int workflowInstanceId,
        string stepId,
        string completedBy,
        string completedByName,
        WorkflowStepAction action,
        string? comments,
        Dictionary<string, object?>? fieldValues);

    /// <summary>
    /// Processes a workflow action (approve/reject/etc.).
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="actionType">The action type (e.g., "approve", "reject").</param>
    /// <param name="userId">The user ID processing the action.</param>
    /// <param name="comments">Optional comments.</param>
    /// <param name="fieldUpdates">Field values for the step.</param>
    /// <returns>The result of the workflow action.</returns>
    Task<WorkflowActionResult> ProcessWorkflowActionAsync(
        int workflowInstanceId,
        string actionType,
        string userId,
        string? comments,
        Dictionary<string, object?>? fieldUpdates);

    /// <summary>
    /// Gets pending workflow steps that a user can act on.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="userRoles">The user's roles.</param>
    /// <returns>List of pending step instances.</returns>
    Task<List<WorkflowStepInstance>> GetPendingStepsForUserAsync(string userId, List<string> userRoles);

    /// <summary>
    /// Gets the current step instance for a workflow.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <returns>The current step instance or null.</returns>
    Task<WorkflowStepInstance?> GetCurrentStepInstanceAsync(int workflowInstanceId);

    /// <summary>
    /// Gets all step instances for a workflow.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <returns>List of step instances.</returns>
    Task<List<WorkflowStepInstance>> GetStepInstancesAsync(int workflowInstanceId);

    /// <summary>
    /// Gets the current workflow step instance for a workflow instance.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <returns>The current workflow step instance or null.</returns>
    Task<WorkflowStepInstance?> GetCurrentWorkflowStepAsync(int workflowInstanceId);

    /// <summary>
    /// Gets completed workflow step instances for a workflow instance.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <returns>List of completed step instances.</returns>
    Task<List<WorkflowStepInstance>> GetCompletedWorkflowStepsAsync(int workflowInstanceId);

    /// <summary>
    /// Checks if a user can access a specific workflow step.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="stepId">The step ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="userRoles">The user's roles.</param>
    /// <returns>True if the user can access the step.</returns>
    Task<bool> CanUserAccessStepAsync(int workflowInstanceId, string stepId, string userId, List<string> userRoles);

    /// <summary>
    /// Gets available steps that a user can navigate to.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <param name="userRoles">The user's roles.</param>
    /// <returns>List of available step IDs.</returns>
    Task<List<string>> GetAvailableStepsForUserAsync(int workflowInstanceId, string userId, List<string> userRoles);
}
