using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories.Queries;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository implementation for WorkflowTransition data access operations.
/// </summary>
public class WorkflowTransitionRepository : IWorkflowTransitionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkflowTransitionRepository> _logger;

    public WorkflowTransitionRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<WorkflowTransitionRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<WorkflowTransition>> GetByWorkflowDefinitionIdAsync(int workflowDefinitionId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            const string sql = @"SELECT * FROM WorkflowTransitions WHERE WorkflowDefinitionId = @WorkflowDefinitionId";
            var transitions = await connection.QueryAsync<WorkflowTransition>(sql, new { WorkflowDefinitionId = workflowDefinitionId });

            _logger.LogDebug("Retrieved {TransitionCount} transitions for workflow definition {DefinitionId}",
                transitions.Count(), workflowDefinitionId);

            return transitions.ToList();
        }
    }

    /// <inheritdoc />
    public async Task<List<WorkflowTransition>> GetFromStepAsync(int workflowInstanceId, string fromStepId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            var transitions = await connection.QueryAsync<WorkflowTransition>(
                WorkflowQueries.GetTransitionsFromStep,
                new { WorkflowInstanceId = workflowInstanceId, CurrentStepId = fromStepId });

            return transitions.ToList();
        }
    }

    /// <inheritdoc />
    public async Task<int> CreateAsync(WorkflowTransition transition, IDbConnection connection, IDbTransaction transaction)
    {
        var id = await connection.QuerySingleAsync<int>(
            WorkflowQueries.CreateTransition,
            new
            {
                transition.WorkflowDefinitionId,
                transition.FromStepId,
                transition.ToStepId,
                transition.Name,
                transition.Condition
            },
            transaction);

        _logger.LogDebug("Created workflow transition from '{FromStepId}' to '{ToStepId}'",
            transition.FromStepId, transition.ToStepId);

        return id;
    }

    /// <inheritdoc />
    public async Task DeleteByWorkflowDefinitionIdAsync(int workflowDefinitionId, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            WorkflowQueries.DeleteTransitionsByDefinitionId,
            new { Id = workflowDefinitionId },
            transaction);

        _logger.LogDebug("Deleted all transitions for workflow definition {DefinitionId}", workflowDefinitionId);
    }
}
