using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service implementation for querying workflow definitions.
/// </summary>
public class WorkflowDefinitionQueryService : IWorkflowDefinitionQueryService
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowStepRepository _stepRepository;
    private readonly ILogger<WorkflowDefinitionQueryService> _logger;

    public WorkflowDefinitionQueryService(
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowStepRepository stepRepository,
        ILogger<WorkflowDefinitionQueryService> logger)
    {
        _definitionRepository = definitionRepository;
        _stepRepository = stepRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(int id)
    {
        return await _definitionRepository.GetByIdAsync(id);
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> GetWorkflowDefinitionByFormAsync(int formDefinitionId)
    {
        return await _definitionRepository.GetByFormDefinitionIdAsync(formDefinitionId);
    }

    /// <inheritdoc />
    public async Task<List<WorkflowDefinition>> GetWorkflowDefinitionsAsync()
    {
        return await _definitionRepository.GetAllAsync();
    }

    /// <inheritdoc />
    public async Task<WorkflowStep?> GetWorkflowStepAsync(int workflowDefinitionId, string stepId)
    {
        return await _stepRepository.GetByStepIdAsync(workflowDefinitionId, stepId);
    }

    /// <inheritdoc />
    public async Task<List<WorkflowStepFieldConfiguration>> GetStepFieldConfigurationsAsync(int workflowDefinitionId, string stepId)
    {
        var step = await _stepRepository.GetByStepIdAsync(workflowDefinitionId, stepId);
        return step?.FieldConfigurations ?? new List<WorkflowStepFieldConfiguration>();
    }
}
