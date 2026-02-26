using Requestr.Core.Models;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Service for approval and rejection of form requests.
/// Handles both legacy approval flow and workflow-based approvals.
/// </summary>
public interface IFormRequestApprovalService
{
    /// <summary>
    /// Approves a form request using the legacy approval flow.
    /// </summary>
    Task<bool> ApproveAsync(int id, string approvedBy, string approvedByName);
    
    /// <summary>
    /// Rejects a form request using the legacy approval flow.
    /// </summary>
    Task<bool> RejectAsync(int id, string rejectedBy, string rejectedByName, string reason);
    
    /// <summary>
    /// Retries a failed form request.
    /// </summary>
    Task<bool> RetryFailedAsync(int id, string retriedBy, string retriedByName);
    
    /// <summary>
    /// Processes a workflow action (approve/reject) for a form request.
    /// </summary>
    Task<bool> ProcessWorkflowActionAsync(int formRequestId, string actionType, string userId, 
        string? comments = null, Dictionary<string, object?>? fieldUpdates = null);
    
    /// <summary>
    /// Gets the current workflow step for a form request.
    /// </summary>
    Task<WorkflowStepInstance?> GetCurrentWorkflowStepAsync(int formRequestId);

    /// <summary>
    /// Gets all current (in-progress) workflow steps for a form request.
    /// Supports parallel execution where multiple steps are active simultaneously.
    /// </summary>
    Task<List<WorkflowStepInstance>> GetCurrentWorkflowStepsAsync(int formRequestId);
    
    /// <summary>
    /// Gets completed workflow steps for a form request.
    /// </summary>
    Task<List<WorkflowStepInstance>> GetCompletedWorkflowStepsAsync(int formRequestId);

    /// <summary>
    /// Processes stuck workflow requests that have completed workflows but haven't been applied.
    /// </summary>
    Task<int> ProcessStuckWorkflowRequestsAsync(string processedBy);
}
