using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories.Queries;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository implementation for WorkflowStep data access operations.
/// </summary>
public class WorkflowStepRepository : IWorkflowStepRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkflowStepRepository> _logger;

    public WorkflowStepRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<WorkflowStepRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<WorkflowStep>> GetByWorkflowDefinitionIdAsync(int workflowDefinitionId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            const string sql = @"
                SELECT ws.*, wfc.Id as ConfigId, wfc.FieldName, wfc.IsRequired as ConfigIsRequired, 
                       wfc.IsReadOnly, wfc.IsHidden
                FROM WorkflowSteps ws
                LEFT JOIN WorkflowStepFieldConfigurations wfc ON ws.Id = wfc.WorkflowStepId
                WHERE ws.WorkflowDefinitionId = @WorkflowDefinitionId
                ORDER BY ws.Id, wfc.Id";

            var stepDict = new Dictionary<int, WorkflowStep>();

            await connection.QueryAsync<WorkflowStepDb, WorkflowStepFieldConfigurationDb?, WorkflowStep>(
                sql,
                (stepDb, configDb) =>
                {
                    if (!stepDict.TryGetValue(stepDb.Id, out var step))
                    {
                        step = MapWorkflowStep(stepDb);
                        stepDict[stepDb.Id] = step;
                    }

                    if (configDb != null && configDb.ConfigId > 0)
                    {
                        step.FieldConfigurations.Add(MapFieldConfiguration(configDb));
                    }

                    return step;
                },
                new { WorkflowDefinitionId = workflowDefinitionId },
                splitOn: "ConfigId");

            _logger.LogDebug("Retrieved {StepCount} steps for workflow definition {DefinitionId}",
                stepDict.Count, workflowDefinitionId);

            return stepDict.Values.ToList();
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowStep?> GetByStepIdAsync(int workflowDefinitionId, string stepId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            const string sql = @"
                SELECT ws.*, wfc.Id as ConfigId, wfc.FieldName, wfc.IsRequired as ConfigIsRequired, 
                       wfc.IsReadOnly, wfc.IsHidden
                FROM WorkflowSteps ws
                LEFT JOIN WorkflowStepFieldConfigurations wfc ON ws.Id = wfc.WorkflowStepId
                WHERE ws.WorkflowDefinitionId = @WorkflowDefinitionId AND ws.StepId = @StepId
                ORDER BY wfc.Id";

            WorkflowStep? result = null;

            await connection.QueryAsync<WorkflowStepDb, WorkflowStepFieldConfigurationDb?, WorkflowStep?>(
                sql,
                (stepDb, configDb) =>
                {
                    if (result == null)
                    {
                        result = MapWorkflowStep(stepDb);
                    }

                    if (configDb != null && configDb.ConfigId > 0)
                    {
                        result.FieldConfigurations.Add(MapFieldConfiguration(configDb));
                    }

                    return result;
                },
                new { WorkflowDefinitionId = workflowDefinitionId, StepId = stepId },
                splitOn: "ConfigId");

            return result;
        }
    }

    /// <inheritdoc />
    public async Task<int> CreateAsync(WorkflowStep step, IDbConnection connection, IDbTransaction transaction)
    {
        var assignedRolesJson = step.AssignedRoles?.Count > 0
            ? JsonSerializer.Serialize(step.AssignedRoles)
            : null;

        var configurationJson = step.Configuration != null
            ? JsonSerializer.Serialize(step.Configuration)
            : null;

        var id = await connection.QuerySingleAsync<int>(
            WorkflowQueries.CreateStep,
            new
            {
                step.WorkflowDefinitionId,
                step.StepId,
                step.StepType,
                step.Name,
                step.Description,
                AssignedRoles = assignedRolesJson,
                step.PositionX,
                step.PositionY,
                Configuration = configurationJson,
                step.IsRequired,
                step.NotificationEmail
            },
            transaction);

        _logger.LogDebug("Created workflow step {StepId} with database ID {Id}", step.StepId, id);
        return id;
    }

    /// <inheritdoc />
    public async Task DeleteByWorkflowDefinitionIdAsync(int workflowDefinitionId, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            WorkflowQueries.DeleteStepsByDefinitionId,
            new { Id = workflowDefinitionId },
            transaction);

        _logger.LogDebug("Deleted all steps for workflow definition {DefinitionId}", workflowDefinitionId);
    }

    /// <inheritdoc />
    public async Task<List<WorkflowStepFieldConfiguration>> GetFieldConfigurationsByStepDbIdAsync(int workflowStepId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var configs = await connection.QueryAsync<dynamic>(
                WorkflowQueries.GetFieldConfigurationsByStepDbId,
                new { WorkflowStepId = workflowStepId });

            var results = new List<WorkflowStepFieldConfiguration>();
            foreach (var config in configs)
            {
                results.Add(new WorkflowStepFieldConfiguration
                {
                    Id = (int)config.Id,
                    WorkflowStepId = (int)config.WorkflowStepId,
                    FieldName = (string)config.FieldName,
                    IsRequired = (bool)config.IsRequired,
                    IsReadOnly = (bool)config.IsReadOnly,
                    IsVisible = (bool)config.IsVisible
                });
            }
            return results;
        }
    }

    private WorkflowStep MapWorkflowStep(WorkflowStepDb stepDb)
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
            NotificationEmail = stepDb.NotificationEmail,
            FieldConfigurations = new List<WorkflowStepFieldConfiguration>()
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

        return step;
    }

    private WorkflowStepFieldConfiguration MapFieldConfiguration(WorkflowStepFieldConfigurationDb configDb)
    {
        return new WorkflowStepFieldConfiguration
        {
            Id = configDb.ConfigId,
            WorkflowStepId = configDb.WorkflowStepId,
            FieldName = configDb.FieldName,
            IsRequired = configDb.ConfigIsRequired,
            IsReadOnly = configDb.IsReadOnly,
            IsVisible = !configDb.IsHidden
        };
    }

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

    private class WorkflowStepFieldConfigurationDb
    {
        public int ConfigId { get; set; }
        public int WorkflowStepId { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public bool ConfigIsRequired { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsHidden { get; set; }
    }
}
