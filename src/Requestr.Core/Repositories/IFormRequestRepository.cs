using Requestr.Core.Models;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository for FormRequest data access operations.
/// Handles all database operations for form requests.
/// </summary>
public interface IFormRequestRepository
{
    #region Read Operations
    
    /// <summary>
    /// Gets a form request by its ID with basic form definition info.
    /// </summary>
    Task<FormRequest?> GetByIdAsync(int id);
    
    /// <summary>
    /// Gets a form request by its ID with full form definition including fields and sections.
    /// </summary>
    Task<FormRequest?> GetByIdWithFormDefinitionAsync(int id);
    
    /// <summary>
    /// Gets all form requests ordered by requested date descending.
    /// </summary>
    Task<List<FormRequest>> GetAllAsync();
    
    /// <summary>
    /// Gets all form requests created by a specific user.
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
    /// Gets pending (non-applied) form requests for a specific form definition.
    /// </summary>
    Task<List<FormRequest>> GetPendingByFormDefinitionAsync(int formDefinitionId);
    
    /// <summary>
    /// Gets form requests that match the approver roles for legacy approval flow.
    /// </summary>
    Task<List<FormRequest>> GetForApprovalAsync(List<string> approverRoles);
    
    /// <summary>
    /// Gets form requests accessible to a user based on their ID and roles.
    /// Includes requests created by the user and requests they can approve.
    /// </summary>
    Task<List<FormRequest>> GetAccessibleByUserAsync(string userId, List<string> userRoles);
    
    /// <summary>
    /// Gets form requests with completed workflows that haven't been applied yet.
    /// Used for diagnostics and recovery of stuck requests.
    /// </summary>
    Task<List<FormRequest>> GetWithCompletedWorkflowsNotAppliedAsync();
    
    /// <summary>
    /// Gets IDs of approved requests that haven't been applied.
    /// </summary>
    Task<List<int>> GetApprovedNotAppliedIdsAsync();
    
    #endregion
    
    #region Write Operations
    
    /// <summary>
    /// Creates a new form request and returns the generated ID.
    /// </summary>
    Task<int> CreateAsync(FormRequest request);
    
    /// <summary>
    /// Creates a new form request within an existing transaction and returns the request with ID set.
    /// </summary>
    Task<FormRequest> CreateAsync(FormRequest request, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Updates an existing form request and returns it.
    /// </summary>
    Task<FormRequest> UpdateAsync(FormRequest request);
    
    /// <summary>
    /// Deletes a form request by ID.
    /// </summary>
    Task<bool> DeleteAsync(int id);
    
    /// <summary>
    /// Updates the status of a form request.
    /// </summary>
    Task UpdateStatusAsync(int id, RequestStatus status);
    
    /// <summary>
    /// Updates a form request to approved status.
    /// </summary>
    Task UpdateToApprovedAsync(int id, string approvedBy, string approvedByName, DateTime approvedAt);
    
    /// <summary>
    /// Approves a form request within an existing transaction.
    /// </summary>
    Task ApproveAsync(int id, string approvedBy, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Rejects a form request.
    /// </summary>
    Task<int> RejectAsync(int id, string rejectedBy, string reason, System.Data.IDbConnection connection, System.Data.IDbTransaction? transaction);
    
    /// <summary>
    /// Updates a form request to rejected status.
    /// </summary>
    Task UpdateToRejectedAsync(int id, string rejectedBy, string rejectedByName, string reason);
    
    /// <summary>
    /// Updates a form request to applied status with the record key.
    /// </summary>
    Task UpdateToAppliedAsync(int id, string? appliedRecordKey);
    
    /// <summary>
    /// Sets a form request as applied within an existing transaction.
    /// </summary>
    Task SetAppliedAsync(int id, string? recordKey, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Sets the applied record key for a form request.
    /// </summary>
    Task SetAppliedRecordKeyAsync(int id, string? recordKey);
    
    /// <summary>
    /// Updates a form request to failed status with the error message.
    /// </summary>
    Task UpdateToFailedAsync(int id, string failureMessage);
    
    /// <summary>
    /// Sets a form request as failed within an existing transaction.
    /// </summary>
    Task SetFailedAsync(int id, string errorMessage, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Updates the status of a form request with an optional failure message.
    /// </summary>
    Task UpdateStatusAsync(int id, RequestStatus status, string? failureMessage);
    
    /// <summary>
    /// Updates the workflow instance ID for a form request within a transaction.
    /// </summary>
    Task UpdateWorkflowInstanceIdAsync(int id, int workflowInstanceId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    #endregion
}
