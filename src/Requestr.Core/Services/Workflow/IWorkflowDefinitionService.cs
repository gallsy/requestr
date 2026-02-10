using Requestr.Core.Models;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service for managing workflow definitions.
/// Handles CRUD operations for workflow templates.
/// </summary>
public interface IWorkflowDefinitionCommandService
{
    /// <summary>
    /// Creates a new workflow definition with steps and transitions.
    /// </summary>
    /// <param name="definition">The workflow definition to create.</param>
    /// <returns>The created workflow definition with its assigned ID.</returns>
    Task<WorkflowDefinition> CreateWorkflowDefinitionAsync(WorkflowDefinition definition);

    /// <summary>
    /// Updates an existing workflow definition, replacing all steps and transitions.
    /// </summary>
    /// <param name="definition">The updated workflow definition.</param>
    /// <returns>True if the update was successful.</returns>
    Task<bool> UpdateWorkflowDefinitionAsync(WorkflowDefinition definition);

    /// <summary>
    /// Deletes a workflow definition by ID.
    /// </summary>
    /// <param name="id">The ID of the workflow definition to delete.</param>
    /// <returns>True if the workflow was deleted.</returns>
    Task<bool> DeleteWorkflowDefinitionAsync(int id);
}

/// <summary>
/// Service for querying workflow definitions.
/// </summary>
public interface IWorkflowDefinitionQueryService
{
    /// <summary>
    /// Gets a workflow definition by ID with all steps and transitions.
    /// </summary>
    /// <param name="id">The workflow definition ID.</param>
    /// <returns>The workflow definition or null if not found.</returns>
    Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(int id);

    /// <summary>
    /// Gets the workflow definition assigned to a form.
    /// </summary>
    /// <param name="formDefinitionId">The form definition ID.</param>
    /// <returns>The workflow definition or null if not found.</returns>
    Task<WorkflowDefinition?> GetWorkflowDefinitionByFormAsync(int formDefinitionId);

    /// <summary>
    /// Gets all workflow definitions.
    /// </summary>
    /// <returns>List of all workflow definitions.</returns>
    Task<List<WorkflowDefinition>> GetWorkflowDefinitionsAsync();

    /// <summary>
    /// Gets a specific step from a workflow definition.
    /// </summary>
    /// <param name="workflowDefinitionId">The workflow definition ID.</param>
    /// <param name="stepId">The step ID within the workflow.</param>
    /// <returns>The workflow step or null if not found.</returns>
    Task<WorkflowStep?> GetWorkflowStepAsync(int workflowDefinitionId, string stepId);

    /// <summary>
    /// Gets field configurations for a specific workflow step.
    /// </summary>
    /// <param name="workflowDefinitionId">The workflow definition ID.</param>
    /// <param name="stepId">The step ID within the workflow.</param>
    /// <returns>List of field configurations for the step.</returns>
    Task<List<WorkflowStepFieldConfiguration>> GetStepFieldConfigurationsAsync(int workflowDefinitionId, string stepId);

    /// <summary>
    /// Gets field configurations for a workflow step by its database ID.
    /// </summary>
    /// <param name="workflowStepId">The database ID of the workflow step.</param>
    /// <returns>List of field configurations for the step.</returns>
    Task<List<WorkflowStepFieldConfiguration>> GetStepFieldConfigurationsByStepIdAsync(int workflowStepId);
}
