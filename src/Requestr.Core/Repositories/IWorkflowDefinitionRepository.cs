using Requestr.Core.Models;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository for WorkflowDefinition data access operations.
/// </summary>
public interface IWorkflowDefinitionRepository
{
    /// <summary>
    /// Gets a workflow definition by ID with all steps and transitions.
    /// </summary>
    Task<WorkflowDefinition?> GetByIdAsync(int id);
    
    /// <summary>
    /// Gets a workflow definition by ID using an existing connection and transaction.
    /// </summary>
    Task<WorkflowDefinition?> GetByIdAsync(int id, System.Data.IDbConnection connection, System.Data.IDbTransaction? transaction);
    
    /// <summary>
    /// Gets the workflow definition assigned to a form.
    /// </summary>
    Task<WorkflowDefinition?> GetByFormDefinitionIdAsync(int formDefinitionId);
    
    /// <summary>
    /// Gets all workflow definitions.
    /// </summary>
    Task<List<WorkflowDefinition>> GetAllAsync();
    
    /// <summary>
    /// Creates a new workflow definition and returns the generated ID.
    /// </summary>
    Task<int> CreateAsync(WorkflowDefinition definition, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Updates an existing workflow definition.
    /// </summary>
    Task UpdateAsync(WorkflowDefinition definition, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Deletes a workflow definition by ID.
    /// </summary>
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// Repository for WorkflowStep data access operations.
/// </summary>
public interface IWorkflowStepRepository
{
    /// <summary>
    /// Gets all steps for a workflow definition.
    /// </summary>
    Task<List<WorkflowStep>> GetByWorkflowDefinitionIdAsync(int workflowDefinitionId);
    
    /// <summary>
    /// Gets a specific step by workflow definition ID and step ID.
    /// </summary>
    Task<WorkflowStep?> GetByStepIdAsync(int workflowDefinitionId, string stepId);
    
    /// <summary>
    /// Gets field configurations for a workflow step by its database ID.
    /// </summary>
    Task<List<WorkflowStepFieldConfiguration>> GetFieldConfigurationsByStepDbIdAsync(int workflowStepId);
    
    /// <summary>
    /// Creates a new workflow step.
    /// </summary>
    Task<int> CreateAsync(WorkflowStep step, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Deletes all steps for a workflow definition.
    /// </summary>
    Task DeleteByWorkflowDefinitionIdAsync(int workflowDefinitionId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
}

/// <summary>
/// Repository for WorkflowTransition data access operations.
/// </summary>
public interface IWorkflowTransitionRepository
{
    /// <summary>
    /// Gets all transitions for a workflow definition.
    /// </summary>
    Task<List<WorkflowTransition>> GetByWorkflowDefinitionIdAsync(int workflowDefinitionId);
    
    /// <summary>
    /// Gets transitions from a specific step.
    /// </summary>
    Task<List<WorkflowTransition>> GetFromStepAsync(int workflowInstanceId, string fromStepId);
    
    /// <summary>
    /// Creates a new workflow transition.
    /// </summary>
    Task<int> CreateAsync(WorkflowTransition transition, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Deletes all transitions for a workflow definition.
    /// </summary>
    Task DeleteByWorkflowDefinitionIdAsync(int workflowDefinitionId, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
}
