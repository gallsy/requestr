using Requestr.Core.Models;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service for querying workflow progress and history.
/// </summary>
public interface IWorkflowProgressService
{
    /// <summary>
    /// Gets the workflow progress for a form request.
    /// </summary>
    /// <param name="formRequestId">The form request ID.</param>
    /// <returns>The workflow progress or null if no workflow exists.</returns>
    Task<WorkflowProgress?> GetWorkflowProgressAsync(int formRequestId);

    /// <summary>
    /// Gets workflow progress for multiple form requests.
    /// </summary>
    /// <param name="formRequestIds">List of form request IDs.</param>
    /// <returns>List of workflow progress objects.</returns>
    Task<List<WorkflowProgress>> GetWorkflowProgressBatchAsync(List<int> formRequestIds);

    /// <summary>
    /// Gets the workflow history for a form request.
    /// </summary>
    /// <param name="formRequestId">The form request ID.</param>
    /// <returns>List of workflow history entries.</returns>
    Task<List<WorkflowHistoryEntry>> GetWorkflowHistoryAsync(int formRequestId);
}
