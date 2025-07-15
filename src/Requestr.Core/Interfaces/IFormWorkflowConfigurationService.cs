using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

/// <summary>
/// Service for managing form-workflow configurations
/// </summary>
public interface IFormWorkflowConfigurationService
{
    /// <summary>
    /// Assign a workflow to a form with default configurations
    /// </summary>
    Task<WorkflowAssignmentDto> AssignWorkflowToFormAsync(int formDefinitionId, int workflowDefinitionId, string assignedBy);
    
    /// <summary>
    /// Remove workflow assignment from a form
    /// </summary>
    Task RemoveWorkflowFromFormAsync(int formDefinitionId, string removedBy);
    
    /// <summary>
    /// Get workflow assignment and configurations for a form
    /// </summary>
    Task<WorkflowAssignmentDto?> GetFormWorkflowConfigurationAsync(int formDefinitionId);
    
    /// <summary>
    /// Update configuration for a specific workflow step in a form
    /// </summary>
    Task UpdateStepConfigurationAsync(int formDefinitionId, string workflowStepId, IStepConfiguration configuration, string updatedBy);
    
    /// <summary>
    /// Get configuration for a specific workflow step in a form
    /// </summary>
    Task<T?> GetStepConfigurationAsync<T>(int formDefinitionId, string workflowStepId) where T : class, IStepConfiguration;
    
    /// <summary>
    /// Validate step configuration against form definition
    /// </summary>
    Task<StepConfigurationValidationResult> ValidateStepConfigurationAsync(int formDefinitionId, string workflowStepId, IStepConfiguration configuration);
    
    /// <summary>
    /// Get all forms using a specific workflow
    /// </summary>
    Task<List<FormDefinition>> GetFormsUsingWorkflowAsync(int workflowDefinitionId);
    
    /// <summary>
    /// Rollback configuration to previous version
    /// </summary>
    Task<bool> RollbackStepConfigurationAsync(int formDefinitionId, string workflowStepId, string rolledBackBy);
    
    /// <summary>
    /// Create default configurations for all steps in a workflow when assigned to a form
    /// </summary>
    Task CreateDefaultConfigurationsAsync(int formDefinitionId, int workflowDefinitionId, string createdBy);
}
