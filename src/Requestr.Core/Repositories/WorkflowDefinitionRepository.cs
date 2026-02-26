using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories.Queries;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository implementation for WorkflowDefinition data access operations.
/// </summary>
public class WorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkflowDefinitionRepository> _logger;

    public WorkflowDefinitionRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<WorkflowDefinitionRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> GetByIdAsync(int id)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            return await GetByIdAsync(id, connection, null);
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> GetByIdAsync(int id, IDbConnection connection, IDbTransaction? transaction)
    {
        using var multi = await connection.QueryMultipleAsync(
            WorkflowQueries.GetDefinitionById, 
            new { Id = id }, 
            transaction);

        var definition = await multi.ReadFirstOrDefaultAsync<WorkflowDefinition>();
        if (definition == null) return null;

        var steps = (await multi.ReadAsync<WorkflowStepDb>()).ToList();
        var transitions = (await multi.ReadAsync<WorkflowTransition>()).ToList();
        var fieldConfigs = (await multi.ReadAsync<WorkflowStepFieldConfigurationDb>()).ToList();

        // Map steps with their field configurations
        definition.Steps = steps.Select(s => MapWorkflowStep(s, fieldConfigs)).ToList();
        definition.Transitions = transitions;

        _logger.LogDebug("Retrieved workflow definition {DefinitionId} with {StepCount} steps and {TransitionCount} transitions",
            id, definition.Steps.Count, definition.Transitions.Count);

        return definition;
    }

    /// <inheritdoc />
    public async Task<WorkflowDefinition?> GetByFormDefinitionIdAsync(int formDefinitionId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            // First get the workflow definition ID for this form
            var definitionId = await connection.QueryFirstOrDefaultAsync<int?>(
                WorkflowQueries.GetDefinitionIdByFormId,
                new { FormDefinitionId = formDefinitionId });

            if (!definitionId.HasValue)
            {
                _logger.LogDebug("No workflow definition found for form definition {FormDefinitionId}", formDefinitionId);
                return null;
            }

            return await GetByIdAsync(definitionId.Value, connection, null);
        }
    }

    /// <inheritdoc />
    public async Task<List<WorkflowDefinition>> GetAllAsync()
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            var definitions = await connection.QueryAsync<WorkflowDefinition>(WorkflowQueries.GetAllDefinitions);
            return definitions.ToList();
        }
    }

    /// <inheritdoc />
    public async Task<int> CreateAsync(WorkflowDefinition definition, IDbConnection connection, IDbTransaction transaction)
    {
        var id = await connection.QuerySingleAsync<int>(
            WorkflowQueries.CreateDefinition,
            new
            {
                FormDefinitionId = definition.FormDefinitionId > 0 ? (int?)definition.FormDefinitionId : null,
                definition.Name,
                definition.Description,
                definition.Version,
                definition.CreatedBy,
                CreatedAt = DateTime.UtcNow
            },
            transaction);

        _logger.LogInformation("Created workflow definition {DefinitionId} with name '{Name}'", id, definition.Name);
        return id;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(WorkflowDefinition definition, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            WorkflowQueries.UpdateDefinition,
            new
            {
                definition.Id,
                definition.Name,
                definition.Description,
                definition.Version,
                definition.UpdatedBy,
                UpdatedAt = DateTime.UtcNow
            },
            transaction);

        _logger.LogInformation("Updated workflow definition {DefinitionId}", definition.Id);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(int id)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            var rowsAffected = await connection.ExecuteAsync(WorkflowQueries.DeleteDefinition, new { Id = id });
            var deleted = rowsAffected > 0;

            if (deleted)
            {
                _logger.LogInformation("Deleted workflow definition {DefinitionId}", id);
            }
            else
            {
                _logger.LogWarning("Workflow definition {DefinitionId} not found for deletion", id);
            }

            return deleted;
        }
    }

    private WorkflowStep MapWorkflowStep(WorkflowStepDb stepDb, List<WorkflowStepFieldConfigurationDb> allConfigs)
    {
        var step = new WorkflowStep
        {
            Id = stepDb.Id,
            WorkflowDefinitionId = stepDb.WorkflowDefinitionId,
            StepId = stepDb.StepId,
            StepType = stepDb.StepType,
            Name = stepDb.Name,
            Description = stepDb.Description ?? string.Empty,
            PositionX = stepDb.PositionX,
            PositionY = stepDb.PositionY,
            IsRequired = stepDb.IsRequired,
            NotificationEmail = stepDb.NotificationEmail
        };

        // Parse AssignedRoles from JSON
        if (!string.IsNullOrEmpty(stepDb.AssignedRoles))
        {
            try
            {
                step.AssignedRoles = JsonSerializer.Deserialize<List<string>>(stepDb.AssignedRoles) ?? new List<string>();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse AssignedRoles for step {StepId}", stepDb.StepId);
                step.AssignedRoles = new List<string>();
            }
        }
        else
        {
            step.AssignedRoles = new List<string>();
        }

        // Parse Configuration from JSON
        if (!string.IsNullOrEmpty(stepDb.Configuration))
        {
            try
            {
                step.Configuration = JsonSerializer.Deserialize<WorkflowStepConfiguration>(stepDb.Configuration);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse Configuration for step {StepId}", stepDb.StepId);
            }
        }

        // Map field configurations for this step
        var stepFieldConfigs = allConfigs.Where(c => c.WorkflowStepId == stepDb.Id).ToList();
        step.FieldConfigurations = stepFieldConfigs.Select(MapFieldConfiguration).ToList();

        return step;
    }

    private WorkflowStepFieldConfiguration MapFieldConfiguration(WorkflowStepFieldConfigurationDb configDb)
    {
        return new WorkflowStepFieldConfiguration
        {
            Id = configDb.Id,
            WorkflowStepId = configDb.WorkflowStepId,
            FieldName = configDb.FieldName,
            IsRequired = configDb.IsRequired,
            IsReadOnly = configDb.IsReadOnly,
            IsVisible = !configDb.IsHidden
        };
    }

    /// <summary>
    /// Database entity for WorkflowStep with JSON string fields.
    /// </summary>
    private class WorkflowStepDb
    {
        public int Id { get; set; }
        public int WorkflowDefinitionId { get; set; }
        public string StepId { get; set; } = string.Empty;
        public WorkflowStepType StepType { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AssignedRoles { get; set; }
        public int PositionX { get; set; }
        public int PositionY { get; set; }
        public string? Configuration { get; set; }
        public bool IsRequired { get; set; }
        public string? NotificationEmail { get; set; }
    }

    /// <summary>
    /// Database entity for WorkflowStepFieldConfiguration.
    /// </summary>
    private class WorkflowStepFieldConfigurationDb
    {
        public int Id { get; set; }
        public int WorkflowStepId { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsHidden { get; set; }
    }
}
