using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using Requestr.Core.Repositories.Queries;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service implementation for querying workflow progress and history.
/// </summary>
public class WorkflowProgressService : IWorkflowProgressService
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<WorkflowProgressService> _logger;

    public WorkflowProgressService(
        IWorkflowDefinitionRepository definitionRepository,
        IDbConnectionFactory connectionFactory,
        ILogger<WorkflowProgressService> logger)
    {
        _definitionRepository = definitionRepository;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkflowProgress?> GetWorkflowProgressAsync(int formRequestId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            // Get workflow progress data
            var result = await connection.QueryFirstOrDefaultAsync<dynamic>(
                WorkflowQueries.GetWorkflowProgress,
                new { FormRequestId = formRequestId });

        if (result == null)
        {
            _logger.LogDebug("No workflow instance found for form request {FormRequestId}", formRequestId);
            return null;
        }

        int workflowInstanceId = result.WorkflowInstanceId;
        int workflowDefinitionId = result.WorkflowDefinitionId;

        // Get step counts
        var counts = await connection.QueryFirstAsync<dynamic>(
            WorkflowQueries.GetStepCounts,
            new
            {
                WorkflowInstanceId = workflowInstanceId,
                CompletedStatus = (int)WorkflowStepInstanceStatus.Completed
            });

        // Get step progress data
        var stepData = await connection.QueryAsync<dynamic>(
            WorkflowQueries.GetStepProgress,
            new { WorkflowInstanceId = workflowInstanceId });

        var steps = stepData.Select(step => new WorkflowStepProgress
        {
            StepId = step.StepId,
            StepName = step.StepName ?? step.StepId,
            StepDescription = step.StepDescription ?? "",
            StepType = (WorkflowStepType)(int)step.StepType,
            Status = ParseStepInstanceStatus(step.Status),
            AssignedTo = step.AssignedTo,
            StartedAt = step.StartedAt,
            CompletedAt = step.CompletedAt,
            CompletedBy = step.CompletedBy,
            CompletedByName = step.CompletedByName,
            Action = step.Action != null ? (WorkflowStepAction)(int)step.Action : null,
            Comments = step.Comments,
            IsCurrent = ((string)result.CurrentStepIds)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(id => id.Equals((string)step.StepId, StringComparison.OrdinalIgnoreCase)),
            DaysInStep = step.StartedAt != null
                ? (int)((step.CompletedAt != null ? (DateTime)step.CompletedAt : DateTime.UtcNow) - ((DateTime)step.StartedAt)).TotalDays
                : 0,
            AssignedRoles = ParseAssignedRoles(step.AssignedRoles)
        }).ToList();

        // Reorder steps by workflow definition sequence
        steps = await ReorderStepsByDefinitionAsync(steps, workflowDefinitionId);

        var currentStepStartedAt = (DateTime?)result.CurrentStepStartedAt;
        var workflowStatus = ParseWorkflowInstanceStatus(result.Status);
        var daysInCurrentStep = (currentStepStartedAt.HasValue && workflowStatus == WorkflowInstanceStatus.InProgress)
            ? (int)(DateTime.UtcNow - currentStepStartedAt.Value).TotalDays
            : 0;

        // Build current step names from all active steps
        var currentStepIdsRaw = (string)result.CurrentStepIds;
        var currentStepIdsList = currentStepIdsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var currentStepNames = steps
            .Where(s => currentStepIdsList.Any(id => id.Equals(s.StepId, StringComparison.OrdinalIgnoreCase)))
            .Select(s => s.StepName)
            .ToList();

        var progress = new WorkflowProgress
        {
            FormRequestId = formRequestId,
            WorkflowInstanceId = workflowInstanceId,
            Status = workflowStatus,
            WorkflowName = (string)result.WorkflowName,
            CurrentStepId = currentStepIdsRaw,
            CurrentStepName = currentStepNames.Count > 0 ? string.Join(", ", currentStepNames) : currentStepIdsRaw,
            CurrentStepStatus = result.CurrentStepStatus != null
                ? ParseStepInstanceStatus(result.CurrentStepStatus)
                : WorkflowStepInstanceStatus.Pending,
            CurrentStepStartedAt = currentStepStartedAt,
            TotalStepsCount = (int)counts.TotalSteps,
            CompletedStepsCount = (int)counts.CompletedSteps,
            WorkflowStartedAt = (DateTime)result.StartedAt,
            WorkflowCompletedAt = (DateTime?)result.CompletedAt,
            CurrentStepAssignedTo = (string?)result.CurrentStepAssignedTo,
            Steps = steps,
            DaysInCurrentStep = daysInCurrentStep,
            IsStalled = daysInCurrentStep > 7
        };

        // Calculate progress percentage
        progress.ProgressPercentage = progress.TotalStepsCount > 0
            ? Math.Round((double)progress.CompletedStepsCount / progress.TotalStepsCount * 100, 1)
            : 0;

        _logger.LogDebug("Retrieved workflow progress for form request {FormRequestId}: {StepCount} steps, {ProgressPercentage}% complete",
            formRequestId, steps.Count, progress.ProgressPercentage);

            return progress;
        }
    }

    /// <inheritdoc />
    public async Task<List<WorkflowProgress>> GetWorkflowProgressBatchAsync(List<int> formRequestIds)
    {
        var results = new List<WorkflowProgress>();

        foreach (var formRequestId in formRequestIds)
        {
            var progress = await GetWorkflowProgressAsync(formRequestId);
            if (progress != null)
            {
                results.Add(progress);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<List<WorkflowHistoryEntry>> GetWorkflowHistoryAsync(int formRequestId)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();

            var data = await connection.QueryAsync<dynamic>(
                WorkflowQueries.GetWorkflowHistory,
                new { FormRequestId = formRequestId });

            return data.Select(h => new WorkflowHistoryEntry
            {
                StepId = h.StepId,
                StepName = h.StepName,
                Status = ParseStepInstanceStatus(h.Status),
                Action = h.Action != null ? (WorkflowStepAction)(int)h.Action : null,
                Comments = h.Comments,
                StartedAt = h.StartedAt,
                CompletedAt = h.CompletedAt,
                CompletedBy = h.CompletedBy,
                CompletedByName = h.CompletedByName
            }).ToList();
        }
    }

    private async Task<List<WorkflowStepProgress>> ReorderStepsByDefinitionAsync(
        List<WorkflowStepProgress> steps,
        int workflowDefinitionId)
    {
        try
        {
            var definition = await _definitionRepository.GetByIdAsync(workflowDefinitionId);
            if (definition == null) return steps;

            // Build adjacency list from transitions
            var graph = new Dictionary<string, List<string>>();
            foreach (var s in definition.Steps)
            {
                if (!graph.ContainsKey(s.StepId)) graph[s.StepId] = new List<string>();
            }
            foreach (var t in definition.Transitions)
            {
                if (!graph.ContainsKey(t.FromStepId)) graph[t.FromStepId] = new List<string>();
                graph[t.FromStepId].Add(t.ToStepId);
            }

            // Find start step and do BFS to assign distances
            var startStep = definition.Steps.FirstOrDefault(s => s.StepType == WorkflowStepType.Start);
            var orderIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (startStep != null)
            {
                var q = new Queue<(string id, int dist)>();
                q.Enqueue((startStep.StepId, 0));
                orderIndex[startStep.StepId] = 0;

                while (q.Count > 0)
                {
                    var (id, dist) = q.Dequeue();
                    if (graph.TryGetValue(id, out var neighbors))
                    {
                        foreach (var n in neighbors)
                        {
                            if (!orderIndex.ContainsKey(n))
                            {
                                orderIndex[n] = dist + 1;
                                q.Enqueue((n, dist + 1));
                            }
                        }
                    }
                }
            }

            int maxAssigned = orderIndex.Values.DefaultIfEmpty(0).Max();

            int GetSortKey(WorkflowStepProgress s)
            {
                if (s.StepType == WorkflowStepType.Start) return -1;
                if (s.StepType == WorkflowStepType.End) return int.MaxValue;
                if (orderIndex.TryGetValue(s.StepId, out var idx)) return idx;
                return maxAssigned + 1;
            }

            return steps
                .OrderBy(s => GetSortKey(s))
                .ThenBy(s => s.StartedAt ?? DateTime.MaxValue)
                .ThenBy(s => s.StepName)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute workflow step order by definition sequence");
            return steps;
        }
    }

    private static List<string> ParseAssignedRoles(object? assignedRolesObj)
    {
        if (assignedRolesObj == null) return new List<string>();

        var json = assignedRolesObj.ToString();
        if (string.IsNullOrEmpty(json)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
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
