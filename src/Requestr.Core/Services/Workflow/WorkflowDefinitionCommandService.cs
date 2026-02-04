using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service implementation for managing workflow definitions.
/// </summary>
public class WorkflowDefinitionCommandService : IWorkflowDefinitionCommandService
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowStepRepository _stepRepository;
    private readonly IWorkflowTransitionRepository _transitionRepository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkflowDefinitionCommandService> _logger;

    public WorkflowDefinitionCommandService(
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowStepRepository stepRepository,
        IWorkflowTransitionRepository transitionRepository,
        IDbConnectionFactory connectionFactory,
        ILogger<WorkflowDefinitionCommandService> logger)
    {
        _definitionRepository = definitionRepository;
        _stepRepository = stepRepository;
        _transitionRepository = transitionRepository;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> CreateWorkflowDefinitionAsync(WorkflowDefinition definition)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Create the workflow definition
                var definitionId = await _definitionRepository.CreateAsync(definition, connection, transaction);

                // Create steps and track their database IDs
                var stepIdMap = new Dictionary<string, int>();
                foreach (var step in definition.Steps)
                {
                    step.WorkflowDefinitionId = definitionId;
                    var stepDbId = await _stepRepository.CreateAsync(step, connection, transaction);
                    stepIdMap[step.StepId] = stepDbId;

                    // Create field configurations for this step
                    foreach (var config in step.FieldConfigurations)
                    {
                        await CreateFieldConfigurationAsync(connection, transaction, stepDbId, config);
                    }
                }

                // Create transitions
                foreach (var transition in definition.Transitions)
                {
                    transition.WorkflowDefinitionId = definitionId;
                    await _transitionRepository.CreateAsync(transition, connection, transaction);
                }

                await transaction.CommitAsync();

                _logger.LogInformation("Created workflow definition {DefinitionId} with {StepCount} steps and {TransitionCount} transitions",
                    definitionId, definition.Steps.Count, definition.Transitions.Count);

                return definitionId;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to create workflow definition '{Name}'", definition.Name);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateWorkflowDefinitionAsync(WorkflowDefinition definition)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Update the definition
                await _definitionRepository.UpdateAsync(definition, connection, transaction);

                // Delete existing steps and transitions (cascade will handle field configs)
                await _transitionRepository.DeleteByWorkflowDefinitionIdAsync(definition.Id, connection, transaction);
                await _stepRepository.DeleteByWorkflowDefinitionIdAsync(definition.Id, connection, transaction);

                // Recreate steps
                foreach (var step in definition.Steps)
                {
                    step.WorkflowDefinitionId = definition.Id;
                    var stepDbId = await _stepRepository.CreateAsync(step, connection, transaction);

                    // Create field configurations for this step
                    foreach (var config in step.FieldConfigurations)
                    {
                        await CreateFieldConfigurationAsync(connection, transaction, stepDbId, config);
                    }
                }

                // Recreate transitions
                foreach (var transition in definition.Transitions)
                {
                    transition.WorkflowDefinitionId = definition.Id;
                    await _transitionRepository.CreateAsync(transition, connection, transaction);
                }

                await transaction.CommitAsync();

                _logger.LogInformation("Updated workflow definition {DefinitionId}", definition.Id);
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update workflow definition {DefinitionId}", definition.Id);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteWorkflowDefinitionAsync(int id)
    {
        return await _definitionRepository.DeleteAsync(id);
    }

    private async Task CreateFieldConfigurationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int workflowStepId,
        WorkflowStepFieldConfiguration config)
    {
        const string sql = @"
            INSERT INTO WorkflowStepFieldConfigurations (WorkflowStepId, FieldName, IsRequired, IsReadOnly, IsVisible)
            VALUES (@WorkflowStepId, @FieldName, @IsRequired, @IsReadOnly, @IsVisible)";

        await Dapper.SqlMapper.ExecuteAsync(connection, sql, new
        {
            WorkflowStepId = workflowStepId,
            config.FieldName,
            config.IsRequired,
            config.IsReadOnly,
            config.IsVisible
        }, transaction);
    }
}
