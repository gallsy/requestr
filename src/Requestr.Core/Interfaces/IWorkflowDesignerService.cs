using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

/// <summary>
/// Service for designing and configuring workflows.
/// Provides step management, transition management, and field configuration.
/// </summary>
public interface IWorkflowDesignerService
{
    /// <summary>
    /// Creates an empty workflow with default start and end steps.
    /// </summary>
    /// <param name="formDefinitionId">The form definition ID (0 for standalone workflow).</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="description">The workflow description.</param>
    /// <returns>The created workflow definition.</returns>
    Task<WorkflowDefinition> CreateEmptyWorkflowAsync(int formDefinitionId, string name, string description);

    /// <summary>
    /// Adds a new step to a workflow.
    /// </summary>
    /// <param name="workflowDefinitionId">The workflow definition ID.</param>
    /// <param name="stepType">The type of step to add.</param>
    /// <param name="name">The step name.</param>
    /// <param name="positionX">The X position in the designer.</param>
    /// <param name="positionY">The Y position in the designer.</param>
    /// <returns>The created workflow step.</returns>
    Task<WorkflowStep> AddStepAsync(int workflowDefinitionId, WorkflowStepType stepType, string name, int positionX, int positionY);

    /// <summary>
    /// Gets the default step configuration for a given step type.
    /// </summary>
    WorkflowStepConfiguration GetDefaultStepConfiguration(WorkflowStepType stepType);

    /// <summary>
    /// Updates an existing workflow step.
    /// </summary>
    /// <param name="stepId">The step database ID.</param>
    /// <param name="stepData">The updated step data.</param>
    /// <returns>The updated workflow step.</returns>
    Task<WorkflowStep> UpdateStepAsync(int stepId, WorkflowStep stepData);

    /// <summary>
    /// Deletes a workflow step.
    /// </summary>
    /// <param name="stepId">The step database ID.</param>
    /// <returns>True if the step was deleted.</returns>
    Task<bool> DeleteStepAsync(int stepId);

    /// <summary>
    /// Adds a transition between two steps.
    /// </summary>
    /// <param name="workflowDefinitionId">The workflow definition ID.</param>
    /// <param name="fromStepId">The source step ID.</param>
    /// <param name="toStepId">The target step ID.</param>
    /// <param name="name">Optional transition name.</param>
    /// <returns>The created transition.</returns>
    Task<WorkflowTransition> AddTransitionAsync(int workflowDefinitionId, string fromStepId, string toStepId, string? name = null);

    /// <summary>
    /// Deletes a workflow transition.
    /// </summary>
    /// <param name="transitionId">The transition database ID.</param>
    /// <returns>True if the transition was deleted.</returns>
    Task<bool> DeleteTransitionAsync(int transitionId);

    /// <summary>
    /// Configures field visibility and requirements for a workflow step.
    /// </summary>
    /// <param name="workflowStepId">The workflow step database ID.</param>
    /// <param name="fieldName">The field name to configure.</param>
    /// <param name="isVisible">Whether the field is visible.</param>
    /// <param name="isReadOnly">Whether the field is read-only.</param>
    /// <param name="isRequired">Whether the field is required.</param>
    /// <returns>The created or updated field configuration.</returns>
    Task<WorkflowStepFieldConfiguration> ConfigureStepFieldAsync(int workflowStepId, string fieldName, bool isVisible, bool isReadOnly, bool isRequired);

    /// <summary>
    /// Deletes a step field configuration.
    /// </summary>
    /// <param name="configurationId">The configuration database ID.</param>
    /// <returns>True if the configuration was deleted.</returns>
    Task<bool> DeleteStepFieldConfigurationAsync(int configurationId);

    /// <summary>
    /// Gets field configurations for a workflow step.
    /// </summary>
    /// <param name="workflowStepId">The workflow step database ID.</param>
    /// <returns>List of field configurations.</returns>
    Task<List<WorkflowStepFieldConfiguration>> GetStepFieldConfigurationsAsync(int workflowStepId);

    /// <summary>
    /// Validates a workflow definition.
    /// </summary>
    /// <param name="workflowDefinitionId">The workflow definition ID.</param>
    /// <returns>List of validation error messages (empty if valid).</returns>
    Task<List<string>> ValidateWorkflowAsync(int workflowDefinitionId);

    /// <summary>
    /// Checks if a workflow is valid.
    /// </summary>
    /// <param name="workflowDefinitionId">The workflow definition ID.</param>
    /// <returns>True if the workflow is valid.</returns>
    Task<bool> IsWorkflowValidAsync(int workflowDefinitionId);
}
