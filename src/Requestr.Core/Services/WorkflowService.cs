using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using System.Text.Json;

namespace Requestr.Core.Services;

// Helper classes for database mapping
internal class WorkflowStepDb : BaseEntity
{
    public int WorkflowDefinitionId { get; set; }
    public string StepId { get; set; } = string.Empty;
    public WorkflowStepType StepType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AssignedRoles { get; set; } // JSON string from database
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public string? Configuration { get; set; } // JSON string from database
    public bool IsRequired { get; set; } = true;
    
    // Convert to domain model
    public WorkflowStep ToDomainModel()
    {
        var step = new WorkflowStep
        {
            Id = this.Id,
            WorkflowDefinitionId = this.WorkflowDefinitionId,
            StepId = this.StepId,
            StepType = this.StepType,
            Name = this.Name,
            Description = this.Description,
            PositionX = this.PositionX,
            PositionY = this.PositionY,
            IsRequired = this.IsRequired
        };
        
        // Deserialize JSON properties
        if (!string.IsNullOrEmpty(this.AssignedRoles))
        {
            try
            {
                step.AssignedRoles = JsonSerializer.Deserialize<List<string>>(this.AssignedRoles) ?? new();
            }
            catch (JsonException)
            {
                step.AssignedRoles = new();
            }
        }
        
        if (!string.IsNullOrEmpty(this.Configuration))
        {
            try
            {
                step.Configuration = JsonSerializer.Deserialize<WorkflowStepConfiguration>(this.Configuration) ?? new();
            }
            catch (JsonException)
            {
                step.Configuration = new();
            }
        }
        
        return step;
    }
}

internal class WorkflowStepFieldConfigurationDb : BaseEntity
{
    public int WorkflowStepId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool IsReadOnly { get; set; } = false;
    public bool IsRequired { get; set; } = false;
    public string? ValidationRules { get; set; } // JSON string from database
    
    // Convert to domain model
    public WorkflowStepFieldConfiguration ToDomainModel()
    {
        var config = new WorkflowStepFieldConfiguration
        {
            Id = this.Id,
            WorkflowStepId = this.WorkflowStepId,
            FieldName = this.FieldName,
            IsVisible = this.IsVisible,
            IsReadOnly = this.IsReadOnly,
            IsRequired = this.IsRequired
        };
        
        // Deserialize JSON properties
        if (!string.IsNullOrEmpty(this.ValidationRules))
        {
            try
            {
                config.ValidationRules = JsonSerializer.Deserialize<List<FieldValidationRule>>(this.ValidationRules) ?? new();
            }
            catch (JsonException)
            {
                config.ValidationRules = new();
            }
        }
        
        return config;
    }
}

public class WorkflowService : IWorkflowService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkflowService> _logger;
    private readonly string _connectionString;

    public WorkflowService(IConfiguration configuration, ILogger<WorkflowService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    #region Workflow Definition Management

    public async Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT * FROM WorkflowDefinitions WHERE Id = @Id;
            
            SELECT * FROM WorkflowSteps WHERE WorkflowDefinitionId = @Id;
            
            SELECT * FROM WorkflowTransitions WHERE WorkflowDefinitionId = @Id;
            
            SELECT wfc.* FROM WorkflowStepFieldConfigurations wfc
            INNER JOIN WorkflowSteps ws ON wfc.WorkflowStepId = ws.Id
            WHERE ws.WorkflowDefinitionId = @Id;";

        using var multi = await connection.QueryMultipleAsync(sql, new { Id = id });
        
        var workflowDefinition = await multi.ReadFirstOrDefaultAsync<WorkflowDefinition>();
        if (workflowDefinition == null) return null;

        var stepsDb = await multi.ReadAsync<WorkflowStepDb>();
        var transitions = await multi.ReadAsync<WorkflowTransition>();
        var fieldConfigurationsDb = await multi.ReadAsync<WorkflowStepFieldConfigurationDb>();

        // Convert database models to domain models
        var steps = stepsDb.Select(s => s.ToDomainModel()).ToList();
        var fieldConfigurations = fieldConfigurationsDb.Select(f => f.ToDomainModel()).ToList();

        // Map relationships
        workflowDefinition.Steps = steps;
        workflowDefinition.Transitions = transitions.ToList();

        foreach (var step in workflowDefinition.Steps)
        {
            step.WorkflowDefinition = workflowDefinition;
            step.FieldConfigurations = fieldConfigurations
                .Where(fc => fc.WorkflowStepId == step.Id)
                .ToList();
        }

        return workflowDefinition;
    }

    public async Task<WorkflowDefinition?> GetWorkflowDefinitionByFormAsync(int formDefinitionId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT Id FROM WorkflowDefinitions WHERE FormDefinitionId = @FormDefinitionId AND IsActive = 1";
        var workflowId = await connection.QueryFirstOrDefaultAsync<int?>(sql, new { FormDefinitionId = formDefinitionId });
        
        return workflowId.HasValue ? await GetWorkflowDefinitionAsync(workflowId.Value) : null;
    }

    public async Task<List<WorkflowDefinition>> GetWorkflowDefinitionsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT * FROM WorkflowDefinitions ORDER BY Name";
        var definitions = await connection.QueryAsync<WorkflowDefinition>(sql);
        
        return definitions.ToList();
    }

    public async Task<WorkflowDefinition> CreateWorkflowDefinitionAsync(WorkflowDefinition workflowDefinition)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        int workflowId = 0;
        bool committed = false;
        
        try
        {
            _logger.LogInformation("Starting creation of workflow definition '{Name}' for form {FormId}", 
                workflowDefinition.Name, workflowDefinition.FormDefinitionId);

            // Validate the workflow definition
            if (workflowDefinition == null)
                throw new ArgumentNullException(nameof(workflowDefinition));
            
            if (string.IsNullOrWhiteSpace(workflowDefinition.Name))
                throw new ArgumentException("Workflow name cannot be empty", nameof(workflowDefinition.Name));
            
            if (workflowDefinition.FormDefinitionId <= 0)
                throw new ArgumentException("Form definition ID must be greater than 0", nameof(workflowDefinition.FormDefinitionId));

            // Insert workflow definition
            const string workflowSql = @"
                INSERT INTO WorkflowDefinitions (FormDefinitionId, Name, Description, IsActive, Version, CreatedBy, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@FormDefinitionId, @Name, @Description, @IsActive, @Version, @CreatedBy, @CreatedAt)";

            _logger.LogDebug("Inserting workflow definition into database");
            workflowId = await connection.QuerySingleAsync<int>(workflowSql, workflowDefinition, transaction);
            workflowDefinition.Id = workflowId;
            
            _logger.LogDebug("Created workflow definition with ID {WorkflowId}", workflowId);

            // Insert steps
            if (workflowDefinition.Steps?.Any() == true)
            {
                _logger.LogDebug("Inserting {StepCount} workflow steps", workflowDefinition.Steps.Count);
                foreach (var step in workflowDefinition.Steps)
                {
                    step.WorkflowDefinitionId = workflowId;
                    await InsertWorkflowStepAsync(connection, transaction, step);
                }
            }

            // Insert transitions
            if (workflowDefinition.Transitions?.Any() == true)
            {
                _logger.LogDebug("Inserting {TransitionCount} workflow transitions", workflowDefinition.Transitions.Count);
                foreach (var transition in workflowDefinition.Transitions)
                {
                    transition.WorkflowDefinitionId = workflowId;
                    await InsertWorkflowTransitionAsync(connection, transaction, transition);
                }
            }

            transaction.Commit();
            committed = true;
            
            _logger.LogInformation("Successfully created workflow definition {WorkflowId} for form {FormId}", workflowId, workflowDefinition.FormDefinitionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating workflow definition '{Name}' for form {FormId}", 
                workflowDefinition?.Name, workflowDefinition?.FormDefinitionId);
            
            if (!committed)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Error rolling back transaction for workflow creation");
                }
            }
            throw;
        }

        // Retrieve the created workflow definition (outside the transaction)
        try
        {
            if (workflowId > 0)
            {
                var result = await GetWorkflowDefinitionAsync(workflowId);
                return result ?? workflowDefinition;
            }
            return workflowDefinition;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving created workflow definition {WorkflowId}, returning original object", workflowId);
            return workflowDefinition;
        }
    }

    public async Task<WorkflowDefinition> UpdateWorkflowDefinitionAsync(WorkflowDefinition workflowDefinition)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        bool committed = false;
        
        try
        {
            // Update workflow definition
            const string workflowSql = @"
                UPDATE WorkflowDefinitions 
                SET Name = @Name, Description = @Description, IsActive = @IsActive, 
                    Version = @Version, UpdatedBy = @UpdatedBy, UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            await connection.ExecuteAsync(workflowSql, workflowDefinition, transaction);

            // Delete existing steps, transitions, and field configurations
            await connection.ExecuteAsync("DELETE FROM WorkflowStepFieldConfigurations WHERE WorkflowStepId IN (SELECT Id FROM WorkflowSteps WHERE WorkflowDefinitionId = @Id)", new { workflowDefinition.Id }, transaction);
            await connection.ExecuteAsync("DELETE FROM WorkflowTransitions WHERE WorkflowDefinitionId = @Id", new { workflowDefinition.Id }, transaction);
            await connection.ExecuteAsync("DELETE FROM WorkflowSteps WHERE WorkflowDefinitionId = @Id", new { workflowDefinition.Id }, transaction);

            // Re-insert steps
            foreach (var step in workflowDefinition.Steps)
            {
                step.WorkflowDefinitionId = workflowDefinition.Id;
                await InsertWorkflowStepAsync(connection, transaction, step);
            }

            // Re-insert transitions
            foreach (var transition in workflowDefinition.Transitions)
            {
                transition.WorkflowDefinitionId = workflowDefinition.Id;
                await InsertWorkflowTransitionAsync(connection, transaction, transition);
            }

            transaction.Commit();
            committed = true;
            
            _logger.LogInformation("Updated workflow definition {WorkflowId}", workflowDefinition.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating workflow definition {WorkflowId}", workflowDefinition?.Id);
            
            if (!committed)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Error rolling back transaction for workflow update");
                }
            }
            throw;
        }

        // Retrieve the updated workflow definition (outside the transaction)
        try
        {
            var result = await GetWorkflowDefinitionAsync(workflowDefinition.Id);
            return result ?? workflowDefinition;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving updated workflow definition {WorkflowId}, returning original object", workflowDefinition.Id);
            return workflowDefinition;
        }
    }

    public async Task<bool> DeleteWorkflowDefinitionAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "DELETE FROM WorkflowDefinitions WHERE Id = @Id";
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
        
        _logger.LogInformation("Deleted workflow definition {WorkflowId}", id);
        
        return rowsAffected > 0;
    }

    public async Task<bool> ActivateWorkflowDefinitionAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "UPDATE WorkflowDefinitions SET IsActive = 1 WHERE Id = @Id";
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
        
        return rowsAffected > 0;
    }

    public async Task<bool> DeactivateWorkflowDefinitionAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "UPDATE WorkflowDefinitions SET IsActive = 0 WHERE Id = @Id";
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = id });
        
        return rowsAffected > 0;
    }

    #endregion

    #region Workflow Instance Management

    public async Task<WorkflowInstance> StartWorkflowAsync(int formRequestId, int workflowDefinitionId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        int instanceId = 0;
        bool committed = false;
        WorkflowInstance? workflowInstance = null;

        try
        {
            var workflowDefinition = await GetWorkflowDefinitionAsync(workflowDefinitionId);
            if (workflowDefinition == null)
                throw new InvalidOperationException($"Workflow definition {workflowDefinitionId} not found");

            // Find start step
            var startStep = workflowDefinition.Steps.FirstOrDefault(s => s.StepType == WorkflowStepType.Start);
            if (startStep == null)
                throw new InvalidOperationException("Workflow has no start step");

            // Create workflow instance
            const string instanceSql = @"
                INSERT INTO WorkflowInstances (FormRequestId, WorkflowDefinitionId, CurrentStepId, Status, StartedAt)
                OUTPUT INSERTED.Id
                VALUES (@FormRequestId, @WorkflowDefinitionId, @CurrentStepId, @Status, @StartedAt)";

            workflowInstance = new WorkflowInstance
            {
                FormRequestId = formRequestId,
                WorkflowDefinitionId = workflowDefinitionId,
                CurrentStepId = startStep.StepId,
                Status = WorkflowInstanceStatus.InProgress,
                StartedAt = DateTime.UtcNow
            };

            instanceId = await connection.QuerySingleAsync<int>(instanceSql, workflowInstance, transaction);
            workflowInstance.Id = instanceId;

            // Create step instances for all steps
            foreach (var step in workflowDefinition.Steps)
            {
                var stepInstance = new WorkflowStepInstance
                {
                    WorkflowInstanceId = instanceId,
                    StepId = step.StepId,
                    Status = step.StepId == startStep.StepId ? WorkflowStepInstanceStatus.InProgress : WorkflowStepInstanceStatus.Pending
                };

                if (step.StepId == startStep.StepId)
                {
                    stepInstance.StartedAt = DateTime.UtcNow;
                }

                await InsertStepInstanceAsync(connection, transaction, stepInstance);
            }

            // If start step is not an approval step, move to next step immediately
            if (startStep.StepType != WorkflowStepType.Approval)
            {
                var nextStepId = await GetNextStepIdAsync(instanceId, startStep.StepId, new Dictionary<string, object?>());
                if (!string.IsNullOrEmpty(nextStepId))
                {
                    await MoveToNextStepAsync(connection, transaction, instanceId, nextStepId);
                }
            }

            transaction.Commit();
            committed = true;
            
            _logger.LogInformation("Started workflow instance {InstanceId} for form request {RequestId}", instanceId, formRequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting workflow instance for request {RequestId}", formRequestId);
            
            if (!committed)
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "Error rolling back transaction for workflow instance creation");
                }
            }
            throw;
        }

        // Retrieve the created workflow instance (outside the transaction)
        try
        {
            if (instanceId > 0)
            {
                var result = await GetWorkflowInstanceAsync(instanceId);
                return result ?? workflowInstance ?? throw new InvalidOperationException("Failed to create workflow instance");
            }
            return workflowInstance ?? throw new InvalidOperationException("Failed to create workflow instance");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving created workflow instance {InstanceId}, returning original object", instanceId);
            return workflowInstance ?? throw new InvalidOperationException("Failed to create workflow instance");
        }
    }

    public async Task<WorkflowInstance?> GetWorkflowInstanceAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT * FROM WorkflowInstances WHERE Id = @Id;
            SELECT * FROM WorkflowStepInstances WHERE WorkflowInstanceId = @Id ORDER BY StartedAt;";

        using var multi = await connection.QueryMultipleAsync(sql, new { Id = id });
        
        var instance = await multi.ReadFirstOrDefaultAsync<WorkflowInstance>();
        if (instance == null) return null;

        var stepInstances = await multi.ReadAsync<WorkflowStepInstance>();
        instance.StepInstances = stepInstances.ToList();

        return instance;
    }

    public async Task<WorkflowInstance?> GetWorkflowInstanceByRequestAsync(int formRequestId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT Id FROM WorkflowInstances WHERE FormRequestId = @FormRequestId";
        var instanceId = await connection.QueryFirstOrDefaultAsync<int?>(sql, new { FormRequestId = formRequestId });
        
        return instanceId.HasValue ? await GetWorkflowInstanceAsync(instanceId.Value) : null;
    }

    public async Task<List<WorkflowInstance>> GetActiveWorkflowInstancesAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT * FROM WorkflowInstances WHERE Status = @Status ORDER BY StartedAt DESC";
        var instances = await connection.QueryAsync<WorkflowInstance>(sql, new { Status = WorkflowInstanceStatus.InProgress });
        
        return instances.ToList();
    }

    public async Task<List<WorkflowInstance>> GetWorkflowInstancesByUserAsync(string userId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT DISTINCT wi.* FROM WorkflowInstances wi
            INNER JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId
            WHERE wsi.AssignedTo = @UserId OR wi.CompletedBy = @UserId
            ORDER BY wi.StartedAt DESC";

        var instances = await connection.QueryAsync<WorkflowInstance>(sql, new { UserId = userId });
        
        return instances.ToList();
    }

    #endregion

    #region Step Processing

    public async Task<bool> CompleteStepAsync(int workflowInstanceId, string stepId, string userId, string userName, WorkflowStepAction action, string? comments = null, Dictionary<string, object?>? fieldValues = null)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Update step instance
            const string updateStepSql = @"
                UPDATE WorkflowStepInstances 
                SET Status = @Status, CompletedAt = @CompletedAt, CompletedBy = @CompletedBy, 
                    CompletedByName = @CompletedByName, Action = @Action, Comments = @Comments, FieldValues = @FieldValues
                WHERE WorkflowInstanceId = @WorkflowInstanceId AND StepId = @StepId";

            await connection.ExecuteAsync(updateStepSql, new
            {
                Status = WorkflowStepInstanceStatus.Completed,
                CompletedAt = DateTime.UtcNow,
                CompletedBy = userId,
                CompletedByName = userName,
                Action = action,
                Comments = comments,
                FieldValues = fieldValues != null ? JsonSerializer.Serialize(fieldValues) : null,
                WorkflowInstanceId = workflowInstanceId,
                StepId = stepId
            }, transaction);

            // If approved, move to next step
            if (action == WorkflowStepAction.Approved || action == WorkflowStepAction.Completed)
            {
                var nextStepId = await GetNextStepIdAsync(workflowInstanceId, stepId, fieldValues ?? new Dictionary<string, object?>());
                if (!string.IsNullOrEmpty(nextStepId))
                {
                    await MoveToNextStepAsync(connection, transaction, workflowInstanceId, nextStepId);
                }
                else
                {
                    // No next step - workflow is complete
                    await CompleteWorkflowAsync(connection, transaction, workflowInstanceId, userId);
                }
            }
            else if (action == WorkflowStepAction.Rejected)
            {
                // Workflow is rejected - mark as completed
                await CompleteWorkflowAsync(connection, transaction, workflowInstanceId, userId);
            }

            transaction.Commit();
            
            _logger.LogInformation("Completed step {StepId} for workflow instance {InstanceId} by user {UserId} with action {Action}", 
                stepId, workflowInstanceId, userId, action);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing step {StepId} for workflow instance {InstanceId}", stepId, workflowInstanceId);
            
            try
            {
                transaction.Rollback();
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Error rolling back transaction for step completion");
            }
            throw;
        }
    }

    public async Task<List<WorkflowStepInstance>> GetPendingStepsForUserAsync(string userId, List<string> userRoles)
    {
        using var connection = new SqlConnection(_connectionString);
        
        // This query needs to check against the workflow steps to find assigned roles
        const string sql = @"
            SELECT wsi.* FROM WorkflowStepInstances wsi
            INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
            INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
            WHERE wsi.Status IN (@InProgress, @Pending)
            AND wi.Status = @WorkflowInProgress
            AND (wsi.AssignedTo = @UserId OR ws.AssignedRoles IS NULL OR ws.AssignedRoles = '[]')";

        var stepInstances = await connection.QueryAsync<WorkflowStepInstance>(sql, new
        {
            UserId = userId,
            InProgress = WorkflowStepInstanceStatus.InProgress,
            Pending = WorkflowStepInstanceStatus.Pending,
            WorkflowInProgress = WorkflowInstanceStatus.InProgress
        });

        // Additional filtering based on assigned roles would need to be done here
        // This is a simplified version - in production, you'd want to optimize this query

        return stepInstances.ToList();
    }

    public async Task<WorkflowStepInstance?> GetCurrentStepInstanceAsync(int workflowInstanceId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT wsi.* FROM WorkflowStepInstances wsi
            INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
            WHERE wi.Id = @WorkflowInstanceId AND wsi.StepId = wi.CurrentStepId";

        return await connection.QueryFirstOrDefaultAsync<WorkflowStepInstance>(sql, new { WorkflowInstanceId = workflowInstanceId });
    }

    public async Task<List<WorkflowStepInstance>> GetStepInstancesAsync(int workflowInstanceId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT * FROM WorkflowStepInstances WHERE WorkflowInstanceId = @WorkflowInstanceId ORDER BY StartedAt";
        var stepInstances = await connection.QueryAsync<WorkflowStepInstance>(sql, new { WorkflowInstanceId = workflowInstanceId });
        
        return stepInstances.ToList();
    }

    #endregion

    #region Step Field Configuration

    public async Task<List<WorkflowStepFieldConfiguration>> GetStepFieldConfigurationsAsync(int workflowStepId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT * FROM WorkflowStepFieldConfigurations WHERE WorkflowStepId = @WorkflowStepId";
        var configurations = await connection.QueryAsync<WorkflowStepFieldConfiguration>(sql, new { WorkflowStepId = workflowStepId });
        
        return configurations.ToList();
    }

    public async Task<Dictionary<string, object>> GetEffectiveFieldConfigurationAsync(int workflowInstanceId, string stepId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT wfc.* FROM WorkflowStepFieldConfigurations wfc
            INNER JOIN WorkflowSteps ws ON wfc.WorkflowStepId = ws.Id
            INNER JOIN WorkflowInstances wi ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId
            WHERE wi.Id = @WorkflowInstanceId AND ws.StepId = @StepId";

        var configurations = await connection.QueryAsync<WorkflowStepFieldConfiguration>(sql, new 
        { 
            WorkflowInstanceId = workflowInstanceId, 
            StepId = stepId 
        });

        var result = new Dictionary<string, object>();
        foreach (var config in configurations)
        {
            result[config.FieldName] = new
            {
                config.IsVisible,
                config.IsReadOnly,
                config.IsRequired,
                config.ValidationRules
            };
        }

        return result;
    }

    #endregion

    #region Workflow Navigation

    public async Task<bool> CanUserAccessStepAsync(string userId, List<string> userRoles, int workflowInstanceId, string stepId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        // Get the step definition to check assigned roles
        const string sql = @"
            SELECT ws.AssignedRoles FROM WorkflowSteps ws
            INNER JOIN WorkflowInstances wi ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId
            WHERE wi.Id = @WorkflowInstanceId AND ws.StepId = @StepId";

        var assignedRolesJson = await connection.QueryFirstOrDefaultAsync<string>(sql, new 
        { 
            WorkflowInstanceId = workflowInstanceId, 
            StepId = stepId 
        });

        if (string.IsNullOrEmpty(assignedRolesJson))
            return true; // No role restrictions

        var assignedRoles = JsonSerializer.Deserialize<List<string>>(assignedRolesJson) ?? new List<string>();
        
        return !assignedRoles.Any() || assignedRoles.Intersect(userRoles).Any();
    }

    public async Task<string?> GetNextStepIdAsync(int workflowInstanceId, string currentStepId, Dictionary<string, object?> formData)
    {
        using var connection = new SqlConnection(_connectionString);
        
        // Get transitions from current step
        const string sql = @"
            SELECT wt.* FROM WorkflowTransitions wt
            INNER JOIN WorkflowInstances wi ON wt.WorkflowDefinitionId = wi.WorkflowDefinitionId
            WHERE wi.Id = @WorkflowInstanceId AND wt.FromStepId = @CurrentStepId";

        var transitions = await connection.QueryAsync<WorkflowTransition>(sql, new 
        { 
            WorkflowInstanceId = workflowInstanceId, 
            CurrentStepId = currentStepId 
        });

        // For now, return the first transition - in a full implementation,
        // you'd evaluate conditions here
        var nextTransition = transitions.FirstOrDefault();
        
        return nextTransition?.ToStepId;
    }

    public async Task<List<string>> GetAvailableStepsForUserAsync(string userId, List<string> userRoles, int workflowInstanceId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        // Get all pending/in-progress steps for this workflow instance
        const string sql = @"
            SELECT DISTINCT wsi.StepId FROM WorkflowStepInstances wsi
            INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
            INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
            WHERE wi.Id = @WorkflowInstanceId 
            AND wsi.Status IN (@Pending, @InProgress)";

        var stepIds = await connection.QueryAsync<string>(sql, new
        {
            WorkflowInstanceId = workflowInstanceId,
            Pending = WorkflowStepInstanceStatus.Pending,
            InProgress = WorkflowStepInstanceStatus.InProgress
        });

        var availableSteps = new List<string>();
        foreach (var stepId in stepIds)
        {
            if (await CanUserAccessStepAsync(userId, userRoles, workflowInstanceId, stepId))
            {
                availableSteps.Add(stepId);
            }
        }

        return availableSteps;
    }

    #endregion

    #region Private Helper Methods

    private async Task InsertWorkflowStepAsync(SqlConnection connection, SqlTransaction transaction, WorkflowStep step)
    {
        const string sql = @"
            INSERT INTO WorkflowSteps (WorkflowDefinitionId, StepId, StepType, Name, Description, AssignedRoles, PositionX, PositionY, Configuration, IsRequired, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@WorkflowDefinitionId, @StepId, @StepType, @Name, @Description, @AssignedRoles, @PositionX, @PositionY, @Configuration, @IsRequired, @CreatedAt)";

        var stepId = await connection.QuerySingleAsync<int>(sql, new
        {
            step.WorkflowDefinitionId,
            step.StepId,
            StepType = step.StepType.ToString(),
            step.Name,
            step.Description,
            AssignedRoles = JsonSerializer.Serialize(step.AssignedRoles),
            step.PositionX,
            step.PositionY,
            Configuration = JsonSerializer.Serialize(step.Configuration),
            step.IsRequired,
            CreatedAt = DateTime.UtcNow
        }, transaction);

        step.Id = stepId;

        // Insert field configurations
        foreach (var fieldConfig in step.FieldConfigurations)
        {
            fieldConfig.WorkflowStepId = stepId;
            await InsertStepFieldConfigurationAsync(connection, transaction, fieldConfig);
        }
    }

    private async Task InsertWorkflowTransitionAsync(SqlConnection connection, SqlTransaction transaction, WorkflowTransition transition)
    {
        const string sql = @"
            INSERT INTO WorkflowTransitions (WorkflowDefinitionId, FromStepId, ToStepId, Condition, Name, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@WorkflowDefinitionId, @FromStepId, @ToStepId, @Condition, @Name, @CreatedAt)";

        var transitionId = await connection.QuerySingleAsync<int>(sql, new
        {
            transition.WorkflowDefinitionId,
            transition.FromStepId,
            transition.ToStepId,
            Condition = transition.Condition != null ? JsonSerializer.Serialize(transition.Condition) : null,
            transition.Name,
            CreatedAt = DateTime.UtcNow
        }, transaction);

        transition.Id = transitionId;
    }

    private async Task InsertStepFieldConfigurationAsync(SqlConnection connection, SqlTransaction transaction, WorkflowStepFieldConfiguration fieldConfig)
    {
        const string sql = @"
            INSERT INTO WorkflowStepFieldConfigurations (WorkflowStepId, FieldName, IsVisible, IsReadOnly, IsRequired, ValidationRules, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@WorkflowStepId, @FieldName, @IsVisible, @IsReadOnly, @IsRequired, @ValidationRules, @CreatedAt)";

        var configId = await connection.QuerySingleAsync<int>(sql, new
        {
            fieldConfig.WorkflowStepId,
            fieldConfig.FieldName,
            fieldConfig.IsVisible,
            fieldConfig.IsReadOnly,
            fieldConfig.IsRequired,
            ValidationRules = JsonSerializer.Serialize(fieldConfig.ValidationRules),
            CreatedAt = DateTime.UtcNow
        }, transaction);

        fieldConfig.Id = configId;
    }

    private async Task InsertStepInstanceAsync(SqlConnection connection, SqlTransaction transaction, WorkflowStepInstance stepInstance)
    {
        const string sql = @"
            INSERT INTO WorkflowStepInstances (WorkflowInstanceId, StepId, Status, AssignedTo, StartedAt, FieldValues)
            OUTPUT INSERTED.Id
            VALUES (@WorkflowInstanceId, @StepId, @Status, @AssignedTo, @StartedAt, @FieldValues)";

        var instanceId = await connection.QuerySingleAsync<int>(sql, new
        {
            stepInstance.WorkflowInstanceId,
            stepInstance.StepId,
            Status = stepInstance.Status.ToString(),
            stepInstance.AssignedTo,
            stepInstance.StartedAt,
            FieldValues = JsonSerializer.Serialize(stepInstance.FieldValues)
        }, transaction);

        stepInstance.Id = instanceId;
    }

    private async Task MoveToNextStepAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string nextStepId)
    {
        // Update workflow instance current step
        const string updateWorkflowSql = "UPDATE WorkflowInstances SET CurrentStepId = @NextStepId WHERE Id = @WorkflowInstanceId";
        await connection.ExecuteAsync(updateWorkflowSql, new { NextStepId = nextStepId, WorkflowInstanceId = workflowInstanceId }, transaction);

        // Update step instance status
        const string updateStepSql = @"
            UPDATE WorkflowStepInstances 
            SET Status = @Status, StartedAt = @StartedAt 
            WHERE WorkflowInstanceId = @WorkflowInstanceId AND StepId = @StepId";

        await connection.ExecuteAsync(updateStepSql, new
        {
            Status = WorkflowStepInstanceStatus.InProgress.ToString(),
            StartedAt = DateTime.UtcNow,
            WorkflowInstanceId = workflowInstanceId,
            StepId = nextStepId
        }, transaction);
    }

    private async Task CompleteWorkflowAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string completedBy)
    {
        const string sql = @"
            UPDATE WorkflowInstances 
            SET Status = @Status, CompletedAt = @CompletedAt, CompletedBy = @CompletedBy 
            WHERE Id = @WorkflowInstanceId";

        await connection.ExecuteAsync(sql, new
        {
            Status = WorkflowInstanceStatus.Completed.ToString(),
            CompletedAt = DateTime.UtcNow,
            CompletedBy = completedBy,
            WorkflowInstanceId = workflowInstanceId
        }, transaction);
    }

    #endregion

    #region Workflow Action Processing

    public async Task<WorkflowActionResult> ProcessWorkflowActionAsync(int workflowInstanceId, string actionType, string userId, string? comments = null, Dictionary<string, object?>? fieldUpdates = null)
    {
        try
        {
            var workflowInstance = await GetWorkflowInstanceAsync(workflowInstanceId);
            if (workflowInstance == null)
            {
                return new WorkflowActionResult
                {
                    Success = false,
                    Message = "Workflow instance not found"
                };
            }

            if (workflowInstance.Status != WorkflowInstanceStatus.InProgress)
            {
                return new WorkflowActionResult
                {
                    Success = false,
                    Message = "Workflow is not in progress"
                };
            }

            var currentStep = await GetCurrentStepInstanceAsync(workflowInstanceId);
            if (currentStep == null)
            {
                return new WorkflowActionResult
                {
                    Success = false,
                    Message = "No current step found"
                };
            }

            // Convert action type to enum
            var action = actionType.ToLower() switch
            {
                "approve" => WorkflowStepAction.Approved,
                "reject" => WorkflowStepAction.Rejected,
                "complete" => WorkflowStepAction.Completed,
                "skip" => WorkflowStepAction.Skipped,
                _ => WorkflowStepAction.Completed
            };

            // Get current step name for result
            var workflowDefinition = await GetWorkflowDefinitionAsync(workflowInstance.WorkflowDefinitionId);
            var currentStepDefinition = workflowDefinition?.Steps.FirstOrDefault(s => s.StepId == currentStep.StepId);
            var previousStepName = currentStepDefinition?.Name ?? currentStep.StepId;

            // Complete the step
            var stepCompleted = await CompleteStepAsync(workflowInstanceId, currentStep.StepId, userId, userId, action, comments, fieldUpdates);
            
            if (!stepCompleted)
            {
                return new WorkflowActionResult
                {
                    Success = false,
                    Message = "Failed to complete workflow step"
                };
            }

            // Refresh workflow instance to get updated status
            workflowInstance = await GetWorkflowInstanceAsync(workflowInstanceId);
            var isCompleted = workflowInstance?.Status == WorkflowInstanceStatus.Completed;
            var isApproved = isCompleted && action == WorkflowStepAction.Approved;
            
            // Get current step name (may be different after step completion)
            string? currentStepName = null;
            if (!isCompleted && workflowInstance != null)
            {
                var newCurrentStep = workflowDefinition?.Steps.FirstOrDefault(s => s.StepId == workflowInstance.CurrentStepId);
                currentStepName = newCurrentStep?.Name ?? workflowInstance.CurrentStepId;
            }

            return new WorkflowActionResult
            {
                Success = true,
                Message = $"Workflow action '{actionType}' completed successfully",
                WorkflowCompleted = isCompleted,
                WorkflowApproved = isApproved,
                PreviousStepName = previousStepName,
                CurrentStepName = currentStepName,
                ActorName = userId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing workflow action {ActionType} for workflow instance {InstanceId}", actionType, workflowInstanceId);
            return new WorkflowActionResult
            {
                Success = false,
                Message = $"Error processing workflow action: {ex.Message}"
            };
        }
    }

    public async Task<WorkflowStepInstance?> GetCurrentWorkflowStepAsync(int workflowInstanceId)
    {
        return await GetCurrentStepInstanceAsync(workflowInstanceId);
    }

    public async Task<List<WorkflowStepInstance>> GetCompletedWorkflowStepsAsync(int workflowInstanceId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT * FROM WorkflowStepInstances 
            WHERE WorkflowInstanceId = @WorkflowInstanceId 
              AND Status = @CompletedStatus
            ORDER BY CompletedAt";
            
        var stepInstances = await connection.QueryAsync<WorkflowStepInstance>(sql, new 
        { 
            WorkflowInstanceId = workflowInstanceId,
            CompletedStatus = WorkflowStepInstanceStatus.Completed.ToString()
        });
        
        return stepInstances.ToList();
    }

    #endregion
}
