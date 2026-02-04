using Requestr.Core.Models;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository for WorkflowInstance data access operations.
/// </summary>
public interface IWorkflowInstanceRepository
{
    /// <summary>
    /// Gets a workflow instance by ID with all step instances.
    /// </summary>
    Task<WorkflowInstance?> GetByIdAsync(int id);
    
    /// <summary>
    /// Gets a workflow instance by ID using an existing connection.
    /// </summary>
    Task<WorkflowInstance?> GetByIdAsync(int id, System.Data.IDbConnection connection, System.Data.IDbTransaction? transaction);
    
    /// <summary>
    /// Gets a workflow instance by form request ID.
    /// </summary>
    Task<WorkflowInstance?> GetByFormRequestIdAsync(int formRequestId);
    
    /// <summary>
    /// Gets all active (in-progress) workflow instances.
    /// </summary>
    Task<List<WorkflowInstance>> GetActiveAsync();
    
    /// <summary>
    /// Gets workflow instances that a user has participated in.
    /// </summary>
    Task<List<WorkflowInstance>> GetByUserAsync(string userId);
    
    /// <summary>
    /// Creates a new workflow instance and returns the generated ID.
    /// </summary>
    Task<int> CreateAsync(WorkflowInstance instance, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Updates the current step of a workflow instance.
    /// </summary>
    Task UpdateCurrentStepAsync(int id, string currentStepId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Marks a workflow instance as completed.
    /// </summary>
    Task UpdateToCompletedAsync(int id, string completedBy, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Marks a workflow instance as failed.
    /// </summary>
    Task UpdateToFailedAsync(int id, string failureReason);
}

/// <summary>
/// Repository for WorkflowStepInstance data access operations.
/// </summary>
public interface IWorkflowStepInstanceRepository
{
    /// <summary>
    /// Gets all step instances for a workflow instance.
    /// </summary>
    Task<List<WorkflowStepInstance>> GetByWorkflowInstanceIdAsync(int workflowInstanceId);
    
    /// <summary>
    /// Gets the current (in-progress) step instance for a workflow.
    /// </summary>
    Task<WorkflowStepInstance?> GetCurrentAsync(int workflowInstanceId);
    
    /// <summary>
    /// Gets completed step instances for a workflow.
    /// </summary>
    Task<List<WorkflowStepInstance>> GetCompletedAsync(int workflowInstanceId);
    
    /// <summary>
    /// Gets pending step instances that a user can act on based on their roles.
    /// </summary>
    Task<List<WorkflowStepInstance>> GetPendingForUserAsync(string userId, List<string> userRoles);
    
    /// <summary>
    /// Creates multiple step instances in bulk.
    /// </summary>
    Task CreateBulkAsync(List<WorkflowStepInstance> instances, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Updates a step instance to in-progress status.
    /// </summary>
    Task UpdateToInProgressAsync(int workflowInstanceId, string stepId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Updates a step instance to completed status.
    /// </summary>
    Task UpdateToCompletedAsync(int workflowInstanceId, string stepId, string completedBy, string completedByName, 
        WorkflowStepAction action, string? comments, Dictionary<string, object?>? fieldValues,
        System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
}
