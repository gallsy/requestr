using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories.Queries;

namespace Requestr.Core.Repositories;

public class WorkflowStepInstanceRepository : IWorkflowStepInstanceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkflowStepInstanceRepository> _logger;

    public WorkflowStepInstanceRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<WorkflowStepInstanceRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<List<WorkflowStepInstance>> GetByWorkflowInstanceIdAsync(int workflowInstanceId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var data = await connection.QueryAsync<dynamic>(
                WorkflowQueries.GetStepInstancesByWorkflowId,
                new { WorkflowInstanceId = workflowInstanceId });

            var results = new List<WorkflowStepInstance>();
            foreach (var d in data)
            {
                results.Add(MapStepInstance(d));
            }
            return results;
        }
    }

    public async Task<WorkflowStepInstance?> GetCurrentAsync(int workflowInstanceId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var data = await connection.QueryFirstOrDefaultAsync<dynamic>(
                WorkflowQueries.GetInProgressStepInstance,
                new
                {
                    WorkflowInstanceId = workflowInstanceId,
                    InProgressStatus = (int)WorkflowStepInstanceStatus.InProgress
                });

            return data != null ? MapStepInstance(data) : null;
        }
    }

    public async Task<List<WorkflowStepInstance>> GetCompletedAsync(int workflowInstanceId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var data = await connection.QueryAsync<dynamic>(
                WorkflowQueries.GetCompletedStepInstances,
                new
                {
                    WorkflowInstanceId = workflowInstanceId,
                    CompletedStatus = (int)WorkflowStepInstanceStatus.Completed
                });

            var results = new List<WorkflowStepInstance>();
            foreach (var d in data)
            {
                results.Add(MapStepInstance(d));
            }
            return results;
        }
    }

    public async Task<List<WorkflowStepInstance>> GetPendingForUserAsync(string userId, List<string> userRoles)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var data = await connection.QueryAsync<dynamic>(
                WorkflowQueries.GetPendingStepsForUser,
                new
                {
                    UserId = userId,
                    InProgress = (int)WorkflowStepInstanceStatus.InProgress,
                    WorkflowInProgress = (int)WorkflowInstanceStatus.InProgress,
                    ApprovalStepType = (int)WorkflowStepType.Approval
                });

            var stepInstances = data.Select(d => MapStepInstanceWithContext(d)).ToList();

            return stepInstances
                .Where(si =>
                {
                    if (si.AssignedRoles == null || si.AssignedRoles.Count == 0)
                        return true;

                    if (!string.IsNullOrEmpty(si.AssignedTo) && si.AssignedTo == userId)
                        return true;

                    return userRoles.Any(role => si.AssignedRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
                })
                .Cast<WorkflowStepInstance>()
                .ToList();
        }
    }

    public async Task CreateBulkAsync(List<WorkflowStepInstance> instances, IDbConnection connection, IDbTransaction transaction)
    {
        foreach (var instance in instances)
        {
            var fieldValuesJson = instance.FieldValues?.Count > 0
                ? JsonSerializer.Serialize(instance.FieldValues)
                : null;

            await connection.ExecuteAsync(
                WorkflowQueries.CreateStepInstance,
                new
                {
                    instance.WorkflowInstanceId,
                    instance.StepId,
                    Status = (int)instance.Status,
                    StartedAt = instance.StartedAt,
                    FieldValues = fieldValuesJson
                },
                transaction);
        }

        var firstInstance = instances.FirstOrDefault();
        if (firstInstance != null)
        {
            int workflowId = firstInstance.WorkflowInstanceId;
            _logger.LogDebug("Created {Count} step instances for workflow {WorkflowInstanceId}",
                instances.Count, workflowId);
        }
    }

    public async Task UpdateToInProgressAsync(int workflowInstanceId, string stepId, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            WorkflowQueries.UpdateStepToInProgress,
            new
            {
                WorkflowInstanceId = workflowInstanceId,
                StepId = stepId,
                Status = (int)WorkflowStepInstanceStatus.InProgress,
                StartedAt = DateTime.UtcNow
            },
            transaction);

        _logger.LogDebug("Updated step {StepId} to InProgress for workflow {WorkflowInstanceId}", stepId, workflowInstanceId);
    }

    public async Task UpdateToCompletedAsync(
        int workflowInstanceId,
        string stepId,
        string completedBy,
        string completedByName,
        WorkflowStepAction action,
        string? comments,
        Dictionary<string, object?>? fieldValues,
        IDbConnection connection,
        IDbTransaction transaction)
    {
        var fieldValuesJson = fieldValues?.Count > 0
            ? JsonSerializer.Serialize(fieldValues)
            : null;

        await connection.ExecuteAsync(
            WorkflowQueries.UpdateStepToCompleted,
            new
            {
                WorkflowInstanceId = workflowInstanceId,
                StepId = stepId,
                Status = (int)WorkflowStepInstanceStatus.Completed,
                CompletedAt = DateTime.UtcNow,
                CompletedBy = completedBy,
                CompletedByName = completedByName,
                Action = (int)action,
                Comments = comments,
                FieldValues = fieldValuesJson
            },
            transaction);

        _logger.LogDebug("Completed step {StepId} with action {Action} for workflow {WorkflowInstanceId}",
            stepId, action, workflowInstanceId);
    }

    private WorkflowStepInstance MapStepInstance(dynamic data)
    {
        int id = (int)data.Id;
        int workflowInstanceId = (int)data.WorkflowInstanceId;
        string stepId = (string)data.StepId;
        string? assignedTo = data.AssignedTo as string;
        DateTime? startedAt = data.StartedAt as DateTime?;
        DateTime? completedAt = data.CompletedAt as DateTime?;
        string? completedBy = data.CompletedBy as string;
        string? completedByName = data.CompletedByName as string;
        string? comments = data.Comments as string;
        string? fieldValuesJson = data.FieldValues as string;

        var instance = new WorkflowStepInstance
        {
            Id = id,
            WorkflowInstanceId = workflowInstanceId,
            StepId = stepId,
            Status = ParseStepInstanceStatus(data.Status),
            AssignedTo = assignedTo,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            CompletedBy = completedBy,
            CompletedByName = completedByName,
            Comments = comments
        };

        if (data.Action != null)
        {
            instance.Action = (WorkflowStepAction)(int)data.Action;
        }

        if (!string.IsNullOrEmpty(fieldValuesJson))
        {
            try
            {
                instance.FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldValuesJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse FieldValues for step instance {StepId}", stepId);
                instance.FieldValues = new Dictionary<string, object?>();
            }
        }

        return instance;
    }

    private WorkflowStepInstanceWithContext MapStepInstanceWithContext(dynamic data)
    {
        int id = (int)data.Id;
        int workflowInstanceId = (int)data.WorkflowInstanceId;
        string stepId = (string)data.StepId;
        string? assignedTo = data.AssignedTo as string;
        DateTime? startedAt = data.StartedAt as DateTime?;
        DateTime? completedAt = data.CompletedAt as DateTime?;
        string? completedBy = data.CompletedBy as string;
        string? comments = data.Comments as string;
        int formRequestId = (int)data.FormRequestId;
        int workflowDefinitionId = (int)data.WorkflowDefinitionId;
        string? assignedRolesJson = data.AssignedRoles as string;

        var instance = new WorkflowStepInstanceWithContext
        {
            Id = id,
            WorkflowInstanceId = workflowInstanceId,
            StepId = stepId,
            Status = ParseStepInstanceStatus(data.Status),
            AssignedTo = assignedTo,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            CompletedBy = completedBy,
            Comments = comments,
            FormRequestId = formRequestId,
            WorkflowDefinitionId = workflowDefinitionId
        };

        if (data.Action != null)
        {
            instance.Action = (WorkflowStepAction)(int)data.Action;
        }

        if (!string.IsNullOrEmpty(assignedRolesJson))
        {
            try
            {
                instance.AssignedRoles = JsonSerializer.Deserialize<List<string>>(assignedRolesJson) ?? new List<string>();
            }
            catch
            {
                instance.AssignedRoles = new List<string>();
            }
        }
        else
        {
            instance.AssignedRoles = new List<string>();
        }

        return instance;
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

    private class WorkflowStepInstanceWithContext : WorkflowStepInstance
    {
        public int FormRequestId { get; set; }
        public int WorkflowDefinitionId { get; set; }
        public List<string> AssignedRoles { get; set; } = new();
    }
}
