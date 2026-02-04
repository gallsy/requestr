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
}
