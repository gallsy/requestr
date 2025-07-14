using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IFormRequestService
{
    Task<List<FormRequest>> GetFormRequestsAsync();
    Task<List<FormRequest>> GetFormRequestsByUserAsync(string userId);
    Task<List<FormRequest>> GetFormRequestsForApprovalAsync(List<string> approverRoles);
    Task<FormRequest?> GetFormRequestAsync(int id);
    Task<FormRequest> CreateFormRequestAsync(FormRequest formRequest);
    Task<FormRequest> UpdateFormRequestAsync(FormRequest formRequest);
    Task<bool> ApproveFormRequestAsync(int id, string approvedBy, string approvedByName);
    Task<bool> RejectFormRequestAsync(int id, string rejectedBy, string rejectedByName, string reason);
    Task<bool> ApplyFormRequestAsync(int id);
    Task<bool> RetryFailedFormRequestAsync(int id, string retriedBy, string retriedByName);
    Task<FormRequest?> GetByIdAsync(int id);
    Task<FormRequest> CreateAsync(FormRequest formRequest);
    Task<FormRequest> UpdateAsync(FormRequest formRequest);
    Task<List<FormRequest>> GetByUserAsync(string userId);
    Task<List<FormRequest>> GetPendingApprovalsAsync(string userId);
    Task<bool> ApproveAsync(int id, string approvedBy, string? comments = null);
    Task<bool> RejectAsync(int id, string rejectedBy, string? comments = null);
    
    // Change tracking methods
    Task<List<FormRequestHistory>> GetFormRequestHistoryAsync(int formRequestId);
    Task<FormRequestHistory> AddFormRequestHistoryAsync(FormRequestHistory history);
    Task<FormRequest?> GetFormRequestWithHistoryAsync(int id);
    Task<string> GetDebugInfoAsync(int formRequestId); // Debug method
    
    // Workflow integration methods
    Task<List<FormRequest>> GetFormRequestsForWorkflowApprovalAsync(string userId, List<string> userRoles);
    Task<bool> ProcessWorkflowActionAsync(int formRequestId, string actionType, string userId, string? comments = null, Dictionary<string, object?>? fieldUpdates = null);
    Task<WorkflowStepInstance?> GetCurrentWorkflowStepAsync(int formRequestId);
    Task<List<WorkflowStepInstance>> GetCompletedWorkflowStepsAsync(int formRequestId);
    Task<List<int>> GetApprovedButNotAppliedRequestIdsAsync();
    Task<bool> ManuallyApplyApprovedRequestAsync(int id);
    
    // Workflow diagnostics - new methods
    Task<List<FormRequest>> GetRequestsWithCompletedWorkflowsButNotAppliedAsync();
    Task<int> ProcessStuckWorkflowRequestsAsync(string processedBy);
    Task<string> GetWorkflowDiagnosticsAsync(int formRequestId);
    
    // Consolidated request access method
    Task<List<FormRequest>> GetAccessibleFormRequestsAsync(string userId, List<string> userRoles);
}
