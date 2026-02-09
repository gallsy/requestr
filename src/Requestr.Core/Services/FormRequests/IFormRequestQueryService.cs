using Requestr.Core.Models;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Service for querying form requests. Read-only operations.
/// </summary>
public interface IFormRequestQueryService
{
    /// <summary>
    /// Gets a form request by ID.
    /// </summary>
    Task<FormRequest?> GetByIdAsync(int id);
    
    /// <summary>
    /// Gets all form requests.
    /// </summary>
    Task<List<FormRequest>> GetAllAsync();
    
    /// <summary>
    /// Gets form requests created by a specific user.
    /// </summary>
    Task<List<FormRequest>> GetByUserAsync(string userId);
    
    /// <summary>
    /// Gets form requests with a specific status.
    /// </summary>
    Task<List<FormRequest>> GetByStatusAsync(RequestStatus status);
    
    /// <summary>
    /// Gets form requests for a specific form definition.
    /// </summary>
    Task<List<FormRequest>> GetByFormDefinitionAsync(int formDefinitionId);
    
    /// <summary>
    /// Gets form requests pending approval based on approver roles.
    /// </summary>
    Task<List<FormRequest>> GetPendingForApprovalAsync(List<string> approverRoles);
    
    /// <summary>
    /// Gets form requests for workflow approval based on user roles.
    /// </summary>
    Task<List<FormRequest>> GetForWorkflowApprovalAsync(string userId, List<string> userRoles);
    
    /// <summary>
    /// Gets form requests with completed workflows that haven't been applied yet.
    /// </summary>
    Task<List<FormRequest>> GetWithCompletedWorkflowsNotAppliedAsync();
    
    /// <summary>
    /// Gets IDs of approved requests that haven't been applied.
    /// </summary>
    Task<List<int>> GetApprovedNotAppliedIdsAsync();

    /// <summary>
    /// Gets form requests accessible to a user based on their roles.
    /// Admin users can see all requests; other users see requests they created
    /// or requests where their roles are assigned to approval steps.
    /// </summary>
    Task<List<FormRequest>> GetAccessibleFormRequestsAsync(string userId, List<string> userRoles);

    /// <summary>
    /// Gets a form request with its history entries.
    /// </summary>
    Task<FormRequest?> GetWithHistoryAsync(int id);

    /// <summary>
    /// Gets pending requests (Pending or Approved status) for a specific form definition.
    /// </summary>
    Task<List<FormRequest>> GetPendingByFormDefinitionAsync(int formDefinitionId);
}
