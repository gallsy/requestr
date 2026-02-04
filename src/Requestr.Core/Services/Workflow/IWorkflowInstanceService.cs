using System.Data;
using Requestr.Core.Models;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service for managing workflow instance lifecycle.
/// Handles starting, querying, and completing workflow instances.
/// </summary>
public interface IWorkflowInstanceService
{
    /// <summary>
    /// Starts a new workflow instance for a form request.
    /// </summary>
    /// <param name="formRequestId">The form request ID.</param>
    /// <param name="workflowDefinitionId">The workflow definition ID.</param>
    /// <param name="initiatedBy">The user ID who initiated the workflow.</param>
    /// <returns>The ID of the created workflow instance.</returns>
    Task<int> StartWorkflowAsync(int formRequestId, int workflowDefinitionId, string initiatedBy);

    /// <summary>
    /// Starts a new workflow instance within an existing transaction.
    /// </summary>
    Task<int> StartWorkflowAsync(
        IDbConnection connection, 
        IDbTransaction transaction, 
        int formRequestId, 
        int workflowDefinitionId, 
        string initiatedBy);

    /// <summary>
    /// Gets a workflow instance by ID.
    /// </summary>
    /// <param name="id">The workflow instance ID.</param>
    /// <returns>The workflow instance or null if not found.</returns>
    Task<WorkflowInstance?> GetWorkflowInstanceAsync(int id);

    /// <summary>
    /// Gets the workflow instance for a specific form request.
    /// </summary>
    /// <param name="formRequestId">The form request ID.</param>
    /// <returns>The workflow instance or null if not found.</returns>
    Task<WorkflowInstance?> GetWorkflowInstanceByRequestAsync(int formRequestId);

    /// <summary>
    /// Gets all active (in-progress) workflow instances.
    /// </summary>
    /// <returns>List of active workflow instances.</returns>
    Task<List<WorkflowInstance>> GetActiveWorkflowInstancesAsync();

    /// <summary>
    /// Gets workflow instances that a user has participated in.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>List of workflow instances.</returns>
    Task<List<WorkflowInstance>> GetWorkflowInstancesByUserAsync(string userId);

    /// <summary>
    /// Marks a workflow instance as failed.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="failureReason">The reason for failure.</param>
    Task FailWorkflowAsync(int workflowInstanceId, string failureReason);

    /// <summary>
    /// Checks if a user has participated in a workflow.
    /// </summary>
    /// <param name="workflowInstanceId">The workflow instance ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>True if the user has participated.</returns>
    Task<bool> HasUserParticipatedInWorkflowAsync(int workflowInstanceId, string userId);
}
