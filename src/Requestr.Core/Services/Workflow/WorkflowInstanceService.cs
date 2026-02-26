using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Repositories;

namespace Requestr.Core.Services.Workflow;

/// <summary>
/// Service implementation for managing workflow instance lifecycle.
/// </summary>
public class WorkflowInstanceService : IWorkflowInstanceService
{
    private readonly IWorkflowInstanceRepository _instanceRepository;
    private readonly IWorkflowStepInstanceRepository _stepInstanceRepository;
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly INotificationService _notificationService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkflowInstanceService> _logger;

    private const string WorkflowStepPending = "WORKFLOW_STEP_PENDING";

    public WorkflowInstanceService(
        IWorkflowInstanceRepository instanceRepository,
        IWorkflowStepInstanceRepository stepInstanceRepository,
        IWorkflowDefinitionRepository definitionRepository,
        IDbConnectionFactory connectionFactory,
        INotificationService notificationService,
        IConfiguration configuration,
        ILogger<WorkflowInstanceService> logger)
    {
        _instanceRepository = instanceRepository;
        _stepInstanceRepository = stepInstanceRepository;
        _definitionRepository = definitionRepository;
        _connectionFactory = connectionFactory;
        _notificationService = notificationService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> StartWorkflowAsync(int formRequestId, int workflowDefinitionId, string initiatedBy)
    {
        var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await using (connection)
        {
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var instanceId = await StartWorkflowAsync(connection, transaction, formRequestId, workflowDefinitionId, initiatedBy);
                await transaction.CommitAsync();
                return instanceId;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> StartWorkflowAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int formRequestId,
        int workflowDefinitionId,
        string initiatedBy)
    {
        // Get the workflow definition
        var definition = await _definitionRepository.GetByIdAsync(workflowDefinitionId, connection, transaction);
        if (definition == null)
        {
            throw new InvalidOperationException($"Workflow definition {workflowDefinitionId} not found");
        }

        // Find the start step
        var startStep = definition.Steps.FirstOrDefault(s => s.StepType == WorkflowStepType.Start);
        if (startStep == null)
        {
            throw new InvalidOperationException($"Workflow definition {workflowDefinitionId} has no start step");
        }

        // Create the workflow instance
        var instance = new WorkflowInstance
        {
            FormRequestId = formRequestId,
            WorkflowDefinitionId = workflowDefinitionId,
            CurrentStepId = startStep.StepId,
            Status = WorkflowInstanceStatus.InProgress
        };

        var instanceId = await _instanceRepository.CreateAsync(instance, connection, transaction);

        // Create step instances for all steps
        var stepInstances = definition.Steps.Select(step => new WorkflowStepInstance
        {
            WorkflowInstanceId = instanceId,
            StepId = step.StepId,
            Status = step.StepId == startStep.StepId
                ? WorkflowStepInstanceStatus.InProgress
                : WorkflowStepInstanceStatus.Pending,
            StartedAt = step.StepId == startStep.StepId ? DateTime.UtcNow : null
        }).ToList();

        await _stepInstanceRepository.CreateBulkAsync(stepInstances, connection, transaction);

        _logger.LogInformation("Started workflow instance {InstanceId} for form request {FormRequestId} using definition {DefinitionId}",
            instanceId, formRequestId, workflowDefinitionId);

        // Auto-complete start step and move to next
        await AutoCompleteStartStepAsync(connection, transaction, instanceId, startStep.StepId, definition);

        return instanceId;
    }

    /// <inheritdoc />
    public async Task<WorkflowInstance?> GetWorkflowInstanceAsync(int id)
    {
        return await _instanceRepository.GetByIdAsync(id);
    }

    /// <inheritdoc />
    public async Task<WorkflowInstance?> GetWorkflowInstanceByRequestAsync(int formRequestId)
    {
        return await _instanceRepository.GetByFormRequestIdAsync(formRequestId);
    }

    /// <inheritdoc />
    public async Task<List<WorkflowInstance>> GetActiveWorkflowInstancesAsync()
    {
        return await _instanceRepository.GetActiveAsync();
    }

    /// <inheritdoc />
    public async Task<List<WorkflowInstance>> GetWorkflowInstancesByUserAsync(string userId)
    {
        return await _instanceRepository.GetByUserAsync(userId);
    }

    /// <inheritdoc />
    public async Task FailWorkflowAsync(int workflowInstanceId, string failureReason)
    {
        await _instanceRepository.UpdateToFailedAsync(workflowInstanceId, failureReason);
        _logger.LogWarning("Workflow instance {InstanceId} marked as failed: {Reason}", workflowInstanceId, failureReason);
    }

    /// <inheritdoc />
    public async Task CancelWorkflowAsync(int workflowInstanceId, string cancelledBy, string reason)
    {
        using var connection = (SqlConnection)_connectionFactory.CreateConnection();
        await connection.OpenAsync();

        // Update the workflow instance to Cancelled
        const string cancelWorkflowSql = @"
            UPDATE WorkflowInstances 
            SET Status = @Status, FailureReason = @Reason, CompletedAt = @CompletedAt
            WHERE Id = @Id";

        await connection.ExecuteAsync(cancelWorkflowSql, new
        {
            Id = workflowInstanceId,
            Status = (int)WorkflowInstanceStatus.Cancelled,
            Reason = reason,
            CompletedAt = DateTime.UtcNow
        });

        // Skip all pending/in-progress step instances
        const string skipStepsSql = @"
            UPDATE WorkflowStepInstances 
            SET Status = @SkippedStatus, CompletedAt = @CompletedAt, CompletedBy = @CancelledBy,
                Comments = @Comments
            WHERE WorkflowInstanceId = @WorkflowInstanceId 
            AND Status IN (@PendingStatus, @InProgressStatus)";

        await connection.ExecuteAsync(skipStepsSql, new
        {
            WorkflowInstanceId = workflowInstanceId,
            SkippedStatus = (int)WorkflowStepInstanceStatus.Skipped,
            PendingStatus = (int)WorkflowStepInstanceStatus.Pending,
            InProgressStatus = (int)WorkflowStepInstanceStatus.InProgress,
            CompletedAt = DateTime.UtcNow,
            CancelledBy = cancelledBy,
            Comments = reason
        });

        _logger.LogInformation("Workflow instance {InstanceId} cancelled by {CancelledBy}: {Reason}", 
            workflowInstanceId, cancelledBy, reason);
    }

    /// <inheritdoc />
    public async Task<bool> HasUserParticipatedInWorkflowAsync(string userId, List<string> userRoles, int workflowInstanceId)
    {
        var instance = await _instanceRepository.GetByIdAsync(workflowInstanceId);
        if (instance == null) return false;

        // Check if user has completed any step
        if (instance.StepInstances.Any(si => si.CompletedBy == userId))
        {
            return true;
        }

        // Check if user is assigned to any step
        if (instance.StepInstances.Any(si => si.AssignedTo == userId))
        {
            return true;
        }

        // Check if any of user's roles are assigned to a step
        var definition = await _definitionRepository.GetByIdAsync(instance.WorkflowDefinitionId);
        if (definition != null)
        {
            foreach (var step in definition.Steps)
            {
                if (step.AssignedRoles?.Any(role => userRoles.Contains(role, StringComparer.OrdinalIgnoreCase)) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task AutoCompleteStartStepAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int workflowInstanceId,
        string stepId,
        WorkflowDefinition definition)
    {
        // Complete the start step
        await _stepInstanceRepository.UpdateToCompletedAsync(
            workflowInstanceId,
            stepId,
            "System",
            WorkflowStepAction.Completed,
            "Auto-completed Start step",
            null,
            connection,
            transaction);

        // Get the next step
        var nextStepId = await GetNextStepIdAsync(connection, transaction, workflowInstanceId, stepId, definition);
        if (!string.IsNullOrEmpty(nextStepId))
        {
            await MoveToNextStepAsync(connection, transaction, workflowInstanceId, nextStepId);

            // Check if next step also needs auto-completion (End step)
            var nextStep = definition.Steps.FirstOrDefault(s => s.StepId == nextStepId);
            if (nextStep?.StepType == WorkflowStepType.End)
            {
                await AutoCompleteEndStepAsync(connection, transaction, workflowInstanceId, nextStepId);
            }
            else if (nextStep != null)
            {
                // Send notification for the new active step
                await SendStepBecomesActiveNotificationAsync((SqlConnection)connection, (SqlTransaction)transaction, workflowInstanceId, nextStepId);
            }
        }

        _logger.LogDebug("Auto-completed start step {StepId} for workflow {InstanceId}", stepId, workflowInstanceId);
    }

    private async Task AutoCompleteEndStepAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int workflowInstanceId,
        string stepId)
    {
        await _stepInstanceRepository.UpdateToCompletedAsync(
            workflowInstanceId,
            stepId,
            "System",
            WorkflowStepAction.Completed,
            "Auto-completed End step",
            null,
            connection,
            transaction);

        await _instanceRepository.UpdateToCompletedAsync(workflowInstanceId, "System", connection, transaction);

        _logger.LogInformation("Workflow {InstanceId} completed at End step {StepId}", workflowInstanceId, stepId);
    }

    private async Task<string?> GetNextStepIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int workflowInstanceId,
        string currentStepId,
        WorkflowDefinition definition)
    {
        // Get transitions from the current step
        var transitions = definition.Transitions
            .Where(t => t.FromStepId.Equals(currentStepId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!transitions.Any())
        {
            _logger.LogDebug("No transitions found from step {StepId}", currentStepId);
            return null;
        }

        // For now, just take the first transition (condition evaluation can be added later)
        return transitions.First().ToStepId;
    }

    private async Task MoveToNextStepAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int workflowInstanceId,
        string nextStepId)
    {
        // Update workflow instance current step
        await _instanceRepository.UpdateCurrentStepAsync(workflowInstanceId, nextStepId, connection, transaction);

        // Update step instance to in-progress
        await _stepInstanceRepository.UpdateToInProgressAsync(workflowInstanceId, nextStepId, connection, transaction);

        _logger.LogDebug("Moved workflow {InstanceId} to step {StepId}", workflowInstanceId, nextStepId);
    }

    private async Task SendStepBecomesActiveNotificationAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string stepId)
    {
        try
        {
            const string sql = @"
                SELECT 
                    wi.Id as WorkflowInstanceId,
                    wi.FormRequestId,
                    fr.BulkFormRequestId,
                    fd.Name as FormName,
                    wd.Name as WorkflowName,
                    fr.RequestType,
                    COALESCE(bfr.Comments, fr.Comments) as RequestComments,
                    fr.RequestedAt,
                    COALESCE(uReq.Email, fr.RequestedBy) as RequestorEmail,
                    COALESCE(uReq.DisplayName, fr.RequestedBy) as RequestedByName,
                    ws.Name as StepName,
                    ws.Description as StepDescription,
                    ws.NotificationEmail as StepNotificationEmail
                FROM WorkflowInstances wi
                INNER JOIN FormRequests fr ON wi.FormRequestId = fr.Id
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                INNER JOIN WorkflowDefinitions wd ON wi.WorkflowDefinitionId = wd.Id
                INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wd.Id AND ws.StepId = @StepId
                LEFT JOIN BulkFormRequests bfr ON fr.BulkFormRequestId = bfr.Id
                LEFT JOIN Users uReq ON uReq.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
                WHERE wi.Id = @WorkflowInstanceId";

            var stepData = await connection.QueryFirstOrDefaultAsync(sql, new
            {
                WorkflowInstanceId = workflowInstanceId,
                StepId = stepId
            }, transaction);

            if (stepData == null || string.IsNullOrWhiteSpace((string?)stepData.StepNotificationEmail))
            {
                _logger.LogDebug("No notification email configured for step {StepId}", stepId);
                return;
            }

            var baseUrl = _configuration["AppBranding:BaseUrl"] ?? "http://localhost:8080";
            int? bulkFormRequestId = stepData.BulkFormRequestId as int?;
            string requestUrl = bulkFormRequestId.HasValue
                ? $"{baseUrl}/bulk-requests/{bulkFormRequestId.Value}"
                : $"{baseUrl}/workflow/{workflowInstanceId}/step/{stepId}";

            var variables = new Dictionary<string, string>
            {
                { "{{RequestId}}", bulkFormRequestId?.ToString() ?? stepData.FormRequestId.ToString() },
                { "{{FormName}}", (stepData.FormName ?? "Form Request").ToString() },
                { "{{WorkflowName}}", (stepData.WorkflowName ?? "Workflow").ToString() },
                { "{{WorkflowStepName}}", (stepData.StepName ?? stepId).ToString() },
                { "{{RequestDescription}}", (stepData.RequestType?.ToString() ?? "Request") },
                { "{{RequestComments}}", (stepData.RequestComments ?? "").ToString() },
                { "{{CreatingUser}}", (stepData.RequestedByName ?? "Unknown").ToString() },
                { "{{CreatingUserEmail}}", (stepData.RequestorEmail ?? "").ToString() },
                { "{{RequestCreatedDate}}", stepData.RequestedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                { "{{RequestUrl}}", requestUrl },
                { "{{SystemName}}", "Requestr" },
                { "{{AssignedUserName}}", "" },
                { "{{AssignedUserEmail}}", "" },
                { "{{DueDate}}", "" }
            };

            await _notificationService.SendNotificationAsync(WorkflowStepPending, variables, (string)stepData.StepNotificationEmail);
            _logger.LogInformation("Sent step pending notification for workflow {InstanceId}, step {StepId} to {Email}",
                workflowInstanceId, stepId, (string)stepData.StepNotificationEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending step active notification for workflow {WorkflowInstanceId}, step {StepId}",
                workflowInstanceId, stepId);
        }
    }
}
