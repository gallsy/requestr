using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories.Queries;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository implementation for WorkflowInstance data access operations.
/// </summary>
public class WorkflowInstanceRepository : IWorkflowInstanceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkflowInstanceRepository> _logger;

    public WorkflowInstanceRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<WorkflowInstanceRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkflowInstance?> GetByIdAsync(int id)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            return await GetByIdAsync(id, connection, null);
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowInstance?> GetByIdAsync(int id, IDbConnection connection, IDbTransaction? transaction)
    {
        using var multi = await connection.QueryMultipleAsync(
            WorkflowQueries.GetInstanceById,
            new { Id = id },
            transaction);

        var instanceData = await multi.ReadFirstOrDefaultAsync<dynamic>();
        if (instanceData == null) return null;

        var stepInstancesData = await multi.ReadAsync<dynamic>();

        var instance = MapWorkflowInstance(instanceData);
        var stepInstances = new List<WorkflowStepInstance>();
        foreach (var d in stepInstancesData)
        {
            stepInstances.Add(MapStepInstance(d));
        }
        instance.StepInstances = stepInstances;

        int stepCount = instance.StepInstances.Count;
        _logger.LogDebug("Retrieved workflow instance {InstanceId} with {StepCount} step instances", id, stepCount);

        return instance;
    }

    /// <inheritdoc />
    public async Task<WorkflowInstance?> GetByFormRequestIdAsync(int formRequestId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var instanceId = await connection.QueryFirstOrDefaultAsync<int?>(
                WorkflowQueries.GetInstanceIdByFormRequestId,
                new { FormRequestId = formRequestId });

            if (!instanceId.HasValue) return null;

            return await GetByIdAsync(instanceId.Value, connection, null);
        }
    }

    /// <inheritdoc />
    public async Task<List<WorkflowInstance>> GetActiveAsync()
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var instances = await connection.QueryAsync<dynamic>(
                WorkflowQueries.GetActiveInstances,
                new { Status = (int)WorkflowInstanceStatus.InProgress });

            var results = new List<WorkflowInstance>();
            foreach (var d in instances)
            {
                results.Add(MapWorkflowInstance(d));
            }
            return results;
        }
    }

    /// <inheritdoc />
    public async Task<List<WorkflowInstance>> GetByUserAsync(string userId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var instances = await connection.QueryAsync<dynamic>(
                WorkflowQueries.GetInstancesByUser,
                new { UserId = userId });

            var results = new List<WorkflowInstance>();
            foreach (var d in instances)
            {
                results.Add(MapWorkflowInstance(d));
            }
            return results;
        }
    }

    /// <inheritdoc />
    public async Task<int> CreateAsync(WorkflowInstance instance, IDbConnection connection, IDbTransaction transaction)
    {
        var id = await connection.QuerySingleAsync<int>(
            WorkflowQueries.CreateInstance,
            new
            {
                instance.FormRequestId,
                instance.WorkflowDefinitionId,
                instance.CurrentStepId,
                Status = (int)instance.Status,
                StartedAt = DateTime.UtcNow
            },
            transaction);

        _logger.LogInformation("Created workflow instance {InstanceId} for form request {FormRequestId}",
            id, instance.FormRequestId);

        return id;
    }

    /// <inheritdoc />
    public async Task UpdateCurrentStepAsync(int id, string currentStepId, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            WorkflowQueries.UpdateInstanceCurrentStep,
            new { Id = id, CurrentStepId = currentStepId },
            transaction);

        _logger.LogDebug("Updated workflow instance {InstanceId} current step to '{StepId}'", id, currentStepId);
    }

    /// <inheritdoc />
    public async Task UpdateToCompletedAsync(int id, string completedBy, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            WorkflowQueries.UpdateInstanceToCompleted,
            new
            {
                Id = id,
                Status = (int)WorkflowInstanceStatus.Completed,
                CompletedAt = DateTime.UtcNow,
                CompletedBy = completedBy
            },
            transaction);

        _logger.LogInformation("Marked workflow instance {InstanceId} as completed by {UserId}", id, completedBy);
    }

    /// <inheritdoc />
    public async Task UpdateToFailedAsync(int id, string failureReason)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            const string sql = @"
                UPDATE WorkflowInstances 
                SET Status = @Status, FailureReason = @FailureReason, CompletedAt = @CompletedAt
                WHERE Id = @Id";

            await connection.ExecuteAsync(sql, new
            {
                Id = id,
                Status = (int)WorkflowInstanceStatus.Failed,
                FailureReason = failureReason,
                CompletedAt = DateTime.UtcNow
            });

            _logger.LogWarning("Marked workflow instance {InstanceId} as failed: {Reason}", id, failureReason);
        }
    }

    private WorkflowInstance MapWorkflowInstance(dynamic data)
    {
        return new WorkflowInstance
        {
            Id = (int)data.Id,
            FormRequestId = (int)data.FormRequestId,
            WorkflowDefinitionId = (int)data.WorkflowDefinitionId,
            CurrentStepId = (string)data.CurrentStepId,
            Status = ParseWorkflowInstanceStatus(data.Status),
            StartedAt = (DateTime)data.StartedAt,
            CompletedAt = (DateTime?)data.CompletedAt,
            CompletedBy = (string?)data.CompletedBy,
            FailureReason = (string?)data.FailureReason
        };
    }

    private WorkflowStepInstance MapStepInstance(dynamic data)
    {
        var instance = new WorkflowStepInstance
        {
            Id = (int)data.Id,
            WorkflowInstanceId = (int)data.WorkflowInstanceId,
            StepId = (string)data.StepId,
            Status = ParseStepInstanceStatus(data.Status),
            AssignedTo = (string?)data.AssignedTo,
            StartedAt = (DateTime?)data.StartedAt,
            CompletedAt = (DateTime?)data.CompletedAt,
            CompletedBy = (string?)data.CompletedBy,
            CompletedByName = (string?)data.CompletedByName,
            Comments = (string?)data.Comments
        };

        if (data.Action != null)
        {
            instance.Action = (WorkflowStepAction)(int)data.Action;
        }

        // Parse FieldValues from JSON
        string? fieldValuesJson = data.FieldValues as string;
        if (!string.IsNullOrEmpty(fieldValuesJson))
        {
            try
            {
                instance.FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldValuesJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse FieldValues for step instance {StepId}", (string)data.StepId);
                instance.FieldValues = new Dictionary<string, object?>();
            }
        }

        return instance;
    }

    private static WorkflowInstanceStatus ParseWorkflowInstanceStatus(object statusValue)
    {
        if (statusValue == null) return WorkflowInstanceStatus.InProgress;

        if (statusValue is int intValue)
        {
            return Enum.IsDefined(typeof(WorkflowInstanceStatus), intValue)
                ? (WorkflowInstanceStatus)intValue
                : WorkflowInstanceStatus.InProgress;
        }

        if (statusValue is string stringValue && Enum.TryParse<WorkflowInstanceStatus>(stringValue, true, out var status))
        {
            return status;
        }

        return WorkflowInstanceStatus.InProgress;
    }

    private static WorkflowStepInstanceStatus ParseStepInstanceStatus(object statusValue)
    {
        if (statusValue == null) return WorkflowStepInstanceStatus.Pending;

        if (statusValue is int intValue)
        {
            return Enum.IsDefined(typeof(WorkflowStepInstanceStatus), intValue)
                ? (WorkflowStepInstanceStatus)intValue
                : WorkflowStepInstanceStatus.Pending;
        }

        if (statusValue is string stringValue && Enum.TryParse<WorkflowStepInstanceStatus>(stringValue, true, out var status))
        {
            return status;
        }

        return WorkflowStepInstanceStatus.Pending;
    }
}
