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
    private readonly IDataService _dataService;
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly string _connectionString;

    public WorkflowService(IConfiguration configuration, ILogger<WorkflowService> logger, IDataService dataService, IFormDefinitionService formDefinitionService)
    {
        _configuration = configuration;
        _logger = logger;
        _dataService = dataService;
        _formDefinitionService = formDefinitionService;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    #region Workflow Definition Management

    public async Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        return await GetWorkflowDefinitionAsync(connection, null, id);
    }

    // Overload that uses existing connection and transaction to avoid deadlocks
    private async Task<WorkflowDefinition?> GetWorkflowDefinitionAsync(SqlConnection connection, SqlTransaction? transaction, int id)
    {
        const string sql = @"
            SELECT * FROM WorkflowDefinitions WHERE Id = @Id;
            
            SELECT * FROM WorkflowSteps WHERE WorkflowDefinitionId = @Id;
            
            SELECT * FROM WorkflowTransitions WHERE WorkflowDefinitionId = @Id;
            
            SELECT wfc.* FROM WorkflowStepFieldConfigurations wfc
            INNER JOIN WorkflowSteps ws ON wfc.WorkflowStepId = ws.Id
            WHERE ws.WorkflowDefinitionId = @Id;";

        try
        {
            using var multi = await connection.QueryMultipleAsync(sql, new { Id = id }, transaction, commandTimeout: 300);
            
            var workflowDefinition = await multi.ReadFirstOrDefaultAsync<WorkflowDefinition>();
            if (workflowDefinition == null) 
            {
                _logger.LogWarning("Workflow definition {WorkflowDefinitionId} not found", id);
                return null;
            }

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

            _logger.LogDebug("Loaded workflow definition {WorkflowDefinitionId} with {StepCount} steps and {TransitionCount} transitions", 
                id, steps.Count, transitions.Count());

            return workflowDefinition;
        }
        catch (SqlException ex) when (ex.Number == -2) // Timeout
        {
            _logger.LogError(ex, "Timeout loading workflow definition {WorkflowDefinitionId}", id);
            throw new TimeoutException($"Timeout loading workflow definition {id}. This may indicate database performance issues.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading workflow definition {WorkflowDefinitionId}", id);
            throw;
        }
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

        try
        {
            var result = await StartWorkflowAsync(formRequestId, workflowDefinitionId, connection, transaction);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // Overload that uses existing connection and transaction to avoid deadlocks
    public async Task<WorkflowInstance> StartWorkflowAsync(int formRequestId, int workflowDefinitionId, SqlConnection connection, SqlTransaction transaction)
    {
        int instanceId = 0;
        WorkflowInstance? workflowInstance = null;

        try
        {
            var workflowDefinition = await GetWorkflowDefinitionAsync(connection, transaction, workflowDefinitionId);
            if (workflowDefinition == null)
                throw new InvalidOperationException($"Workflow definition {workflowDefinitionId} not found");

            // Find start step
            var startStep = workflowDefinition.Steps.FirstOrDefault(s => s.StepType == WorkflowStepType.Start);
            if (startStep == null)
                throw new InvalidOperationException("Workflow has no start step");

            // Create workflow instance with increased timeout
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

            // Increased timeout and added retry logic
            var maxRetries = 3;
            var retryDelay = TimeSpan.FromSeconds(1);
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    instanceId = await connection.QuerySingleAsync<int>(instanceSql, new 
                    {
                        workflowInstance.FormRequestId,
                        workflowInstance.WorkflowDefinitionId,
                        workflowInstance.CurrentStepId,
                        Status = (int)workflowInstance.Status,
                        workflowInstance.StartedAt
                    }, transaction, commandTimeout: 300);
                    break;
                }
                catch (SqlException ex) when (ex.Number == -2 && attempt < maxRetries) // Timeout
                {
                    _logger.LogWarning("Workflow instance creation timeout on attempt {Attempt}/{MaxRetries} for request {RequestId}", 
                        attempt, maxRetries, formRequestId);
                    await Task.Delay(retryDelay * attempt);
                }
            }
            
            workflowInstance.Id = instanceId;

            _logger.LogInformation("Workflow definition has {StepCount} steps: {StepIds}", 
                workflowDefinition.Steps.Count, string.Join(", ", workflowDefinition.Steps.Select(s => $"{s.StepId}({s.StepType})")));

            // Create step instances for all steps using bulk operation
            await CreateStepInstancesBulkAsync(connection, transaction, instanceId, workflowDefinition.Steps, startStep.StepId);

            // Start step should automatically complete when reached
            if (startStep.StepType == WorkflowStepType.Start)
            {
                _logger.LogInformation("Start step '{StepId}' reached, automatically completing", startStep.StepId);
                await AutoCompleteStepAsync(connection, transaction, instanceId, startStep.StepId, "System", "Start");
                
                var nextStepId = await GetNextStepIdAsync(connection, transaction, instanceId, startStep.StepId, new Dictionary<string, object?>());
                if (!string.IsNullOrEmpty(nextStepId))
                {
                    _logger.LogInformation("Moving workflow instance {InstanceId} from '{CurrentStep}' to '{NextStep}'", instanceId, startStep.StepId, nextStepId);
                    await MoveToNextStepAsync(connection, transaction, instanceId, nextStepId);
                    
                    // Check if the next step is an End step and auto-complete it
                    await HandleStepTypeAutoCompletion(connection, transaction, instanceId, nextStepId, workflowDefinition);
                }
                else
                {
                    _logger.LogWarning("No next step found for workflow instance {InstanceId} from start step '{StepId}'", instanceId, startStep.StepId);
                }
            }
            else if (startStep.StepType != WorkflowStepType.Approval)
            {
                _logger.LogInformation("Start step '{StepId}' is type '{StepType}', attempting to move to next step", startStep.StepId, startStep.StepType);
                var nextStepId = await GetNextStepIdAsync(connection, transaction, instanceId, startStep.StepId, new Dictionary<string, object?>());
                if (!string.IsNullOrEmpty(nextStepId))
                {
                    _logger.LogInformation("Moving workflow instance {InstanceId} from '{CurrentStep}' to '{NextStep}'", instanceId, startStep.StepId, nextStepId);
                    await MoveToNextStepAsync(connection, transaction, instanceId, nextStepId);
                }
                else
                {
                    _logger.LogWarning("No next step found for workflow instance {InstanceId} from start step '{StepId}'", instanceId, startStep.StepId);
                }
            }
            else
            {
                _logger.LogInformation("Start step '{StepId}' is an approval step, staying on this step", startStep.StepId);
            }

            _logger.LogInformation("Started workflow instance {InstanceId} for form request {RequestId}", instanceId, formRequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting workflow instance for request {RequestId}", formRequestId);
            throw;
        }

        // Return the workflow instance we created
        if (workflowInstance != null && instanceId > 0)
        {
            // Load step instances that were just created using the same connection and transaction
            try
            {
                const string stepInstancesSql = "SELECT * FROM WorkflowStepInstances WHERE WorkflowInstanceId = @Id ORDER BY StartedAt";
                var stepInstances = await connection.QueryAsync<WorkflowStepInstance>(stepInstancesSql, new { Id = instanceId }, transaction);
                workflowInstance.StepInstances = stepInstances.ToList();
                _logger.LogInformation("Loaded {Count} step instances for workflow instance {InstanceId}", stepInstances.Count(), instanceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading step instances for workflow instance {InstanceId}", instanceId);
                workflowInstance.StepInstances = new List<WorkflowStepInstance>();
            }
            
            return workflowInstance;
        }
        
        throw new InvalidOperationException("Failed to create workflow instance");
    }

    public async Task<WorkflowInstance?> GetWorkflowInstanceAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT * FROM WorkflowInstances WHERE Id = @Id;
            SELECT * FROM WorkflowStepInstances WHERE WorkflowInstanceId = @Id ORDER BY ISNULL(StartedAt, '1900-01-01'), Id;";

        try
        {
            using var multi = await connection.QueryMultipleAsync(sql, new { Id = id }, commandTimeout: 60);
            
            var workflowInstanceData = await multi.ReadFirstOrDefaultAsync();
            if (workflowInstanceData == null) return null;

            // Manually map the workflow instance to handle enum conversion
            var workflowInstance = new WorkflowInstance
            {
                Id = workflowInstanceData.Id,
                FormRequestId = workflowInstanceData.FormRequestId,
                WorkflowDefinitionId = workflowInstanceData.WorkflowDefinitionId,
                CurrentStepId = workflowInstanceData.CurrentStepId,
                Status = ParseWorkflowInstanceStatusFromObject(workflowInstanceData.Status),
                StartedAt = workflowInstanceData.StartedAt,
                CompletedAt = workflowInstanceData.CompletedAt,
                CompletedBy = workflowInstanceData.CompletedBy,
                FailureReason = workflowInstanceData.FailureReason
            };

            var stepInstancesData = await multi.ReadAsync();
            var stepInstances = stepInstancesData.Select(stepData => new WorkflowStepInstance
            {
                Id = stepData.Id,
                WorkflowInstanceId = stepData.WorkflowInstanceId,
                StepId = stepData.StepId,
                Status = ParseWorkflowStepInstanceStatus(stepData.Status),
                AssignedTo = stepData.AssignedTo,
                StartedAt = stepData.StartedAt,
                CompletedAt = stepData.CompletedAt,
                CompletedBy = stepData.CompletedBy,
                CompletedByName = stepData.CompletedByName,
                Action = ParseWorkflowStepAction(stepData.Action),
                Comments = stepData.Comments,
                FieldValues = !string.IsNullOrEmpty(stepData.FieldValues) 
                    ? JsonSerializer.Deserialize<Dictionary<string, object?>>(stepData.FieldValues) ?? new Dictionary<string, object?>() 
                    : new Dictionary<string, object?>()
            }).ToList();

            workflowInstance.StepInstances = stepInstances;

            return workflowInstance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading workflow instance {WorkflowInstanceId}", id);
            throw;
        }
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
        var instancesData = await connection.QueryAsync(sql, new { Status = (int)WorkflowInstanceStatus.InProgress });
        
        var instances = instancesData.Select(data => new WorkflowInstance
        {
            Id = data.Id,
            FormRequestId = data.FormRequestId,
            WorkflowDefinitionId = data.WorkflowDefinitionId,
            CurrentStepId = data.CurrentStepId,
            Status = ParseWorkflowInstanceStatusFromObject(data.Status),
            StartedAt = data.StartedAt,
            CompletedAt = data.CompletedAt,
            CompletedBy = data.CompletedBy,
            FailureReason = data.FailureReason
        }).ToList();
        
        return instances;
    }

    public async Task<List<WorkflowInstance>> GetWorkflowInstancesByUserAsync(string userId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT DISTINCT wi.* FROM WorkflowInstances wi
            INNER JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId
            WHERE wsi.AssignedTo = @UserId OR wi.CompletedBy = @UserId
            ORDER BY wi.StartedAt DESC";

        var instancesData = await connection.QueryAsync(sql, new { UserId = userId });
        
        var instances = instancesData.Select(data => new WorkflowInstance
        {
            Id = data.Id,
            FormRequestId = data.FormRequestId,
            WorkflowDefinitionId = data.WorkflowDefinitionId,
            CurrentStepId = data.CurrentStepId,
            Status = ParseWorkflowInstanceStatusFromObject(data.Status),
            StartedAt = data.StartedAt,
            CompletedAt = data.CompletedAt,
            CompletedBy = data.CompletedBy,
            FailureReason = data.FailureReason
        }).ToList();
        
        return instances;
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

            // Handle different step actions
            if (action == WorkflowStepAction.Rejected)
            {
                // When an approval is rejected, stop the workflow and update request status
                _logger.LogInformation("Step {StepId} rejected by {UserId}, stopping workflow and updating request status", 
                    stepId, userId);
                
                await RejectWorkflowAsync(connection, transaction, workflowInstanceId, userId, comments);
            }
            else if (action == WorkflowStepAction.Approved || action == WorkflowStepAction.Completed)
            {
                // Get workflow definition to check step types
                var workflowInstance = await GetWorkflowInstanceAsync(connection, transaction, workflowInstanceId);
                var workflowDefinition = await GetWorkflowDefinitionAsync(connection, transaction, workflowInstance.WorkflowDefinitionId);
                
                if (workflowDefinition == null)
                {
                    throw new InvalidOperationException($"Workflow definition {workflowInstance.WorkflowDefinitionId} not found");
                }
                
                var nextStepId = await GetNextStepIdAsync(workflowInstanceId, stepId, fieldValues ?? new Dictionary<string, object?>());
                if (!string.IsNullOrEmpty(nextStepId))
                {
                    _logger.LogInformation("Moving workflow {WorkflowInstanceId} to next step: {NextStepId}", workflowInstanceId, nextStepId);
                    await MoveToNextStepAsync(connection, transaction, workflowInstanceId, nextStepId);
                    
                    // Handle auto-completion for specific step types
                    _logger.LogInformation("Handling auto-completion for step type of step: {NextStepId}", nextStepId);
                    await HandleStepTypeAutoCompletion(connection, transaction, workflowInstanceId, nextStepId, workflowDefinition);
                }
                else
                {
                    // No next step - workflow is complete
                    _logger.LogInformation("No next step found, completing workflow {WorkflowInstanceId}", workflowInstanceId);
                    await CompleteWorkflowAsync(connection, transaction, workflowInstanceId, userId);
                }
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
        
        // This query finds workflow step instances that are pending or in-progress
        // and checks if the user can access them based on assigned roles or direct assignment
        // Note: Also handles NULL status values for backwards compatibility
        const string sql = @"
            SELECT wsi.*, wi.FormRequestId, wi.WorkflowDefinitionId
            FROM WorkflowStepInstances wsi
            INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
            INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
            WHERE (wsi.Status IN (@InProgress, @Pending) OR wsi.Status IS NULL)
            AND wi.Status = @WorkflowInProgress
            AND ws.StepType = @ApprovalStepType
            AND (
                wsi.AssignedTo = @UserId 
                OR wsi.AssignedTo IS NULL
                OR ws.AssignedRoles IS NULL 
                OR ws.AssignedRoles = '[]'
                OR ws.AssignedRoles = ''
            )
            ORDER BY ISNULL(wsi.StartedAt, '1900-01-01'), wsi.Id ASC";

        var stepInstances = await connection.QueryAsync<WorkflowStepInstance>(sql, new
        {
            UserId = userId,
            InProgress = WorkflowStepInstanceStatus.InProgress,
            Pending = WorkflowStepInstanceStatus.Pending,
            WorkflowInProgress = WorkflowInstanceStatus.InProgress,
            ApprovalStepType = WorkflowStepType.Approval
        });

        var result = new List<WorkflowStepInstance>();

        // Additional filtering for role-based access
        foreach (var stepInstance in stepInstances)
        {
            // If directly assigned to user, include it
            if (stepInstance.AssignedTo == userId)
            {
                result.Add(stepInstance);
                continue;
            }

            // Check if user has required roles
            var workflowStep = await GetWorkflowStepAsync(stepInstance.WorkflowInstanceId, stepInstance.StepId);
            if (workflowStep != null)
            {
                if (workflowStep.AssignedRoles == null || workflowStep.AssignedRoles.Count == 0)
                {
                    // No specific roles required - any authenticated user can access
                    result.Add(stepInstance);
                }
                else if (userRoles != null && workflowStep.AssignedRoles.Any(role => userRoles.Contains(role)))
                {
                    // User has at least one of the required roles
                    result.Add(stepInstance);
                }
            }
        }

        _logger.LogInformation("Found {Count} pending workflow steps for user {UserId}", result.Count, userId);
        return result;
    }

    public async Task<WorkflowStepInstance?> GetCurrentStepInstanceAsync(int workflowInstanceId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        _logger.LogInformation("GetCurrentStepInstanceAsync called for workflowInstanceId: {WorkflowInstanceId}", workflowInstanceId);
        
        // First try to get the current step using CurrentStepId
        const string sqlCurrentStep = @"
            SELECT wsi.* FROM WorkflowStepInstances wsi
            INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
            WHERE wi.Id = @WorkflowInstanceId AND wsi.StepId = wi.CurrentStepId";

        var currentStepData = await connection.QueryFirstOrDefaultAsync(sqlCurrentStep, new { WorkflowInstanceId = workflowInstanceId });
        
        if (currentStepData != null)
        {
            var currentStep = new WorkflowStepInstance
            {
                Id = currentStepData.Id,
                WorkflowInstanceId = currentStepData.WorkflowInstanceId,
                StepId = currentStepData.StepId,
                Status = ParseWorkflowStepInstanceStatus(currentStepData.Status),
                AssignedTo = currentStepData.AssignedTo,
                StartedAt = currentStepData.StartedAt,
                CompletedAt = currentStepData.CompletedAt,
                CompletedBy = currentStepData.CompletedBy,
                CompletedByName = currentStepData.CompletedByName,
                Action = ParseWorkflowStepAction(currentStepData.Action),
                Comments = currentStepData.Comments,
                FieldValues = !string.IsNullOrEmpty(currentStepData.FieldValues) 
                    ? JsonSerializer.Deserialize<Dictionary<string, object?>>(currentStepData.FieldValues) ?? new Dictionary<string, object?>() 
                    : new Dictionary<string, object?>()
            };
            
            _logger.LogInformation("Found current step using CurrentStepId: {StepId}", currentStep.StepId);
            return currentStep;
        }
        
        _logger.LogInformation("No current step found using CurrentStepId, trying InProgress status");
        
        // Fallback: Look for InProgress step instances
        const string sqlInProgressStep = @"
            SELECT wsi.* FROM WorkflowStepInstances wsi
            WHERE wsi.WorkflowInstanceId = @WorkflowInstanceId 
            AND wsi.Status = @InProgressStatus
            ORDER BY wsi.StartedAt DESC";

        var inProgressStepData = await connection.QueryFirstOrDefaultAsync(sqlInProgressStep, new { 
            WorkflowInstanceId = workflowInstanceId,
            InProgressStatus = (int)WorkflowStepInstanceStatus.InProgress
        });
        
        if (inProgressStepData != null)
        {
            var inProgressStep = new WorkflowStepInstance
            {
                Id = inProgressStepData.Id,
                WorkflowInstanceId = inProgressStepData.WorkflowInstanceId,
                StepId = inProgressStepData.StepId,
                Status = ParseWorkflowStepInstanceStatus(inProgressStepData.Status),
                AssignedTo = inProgressStepData.AssignedTo,
                StartedAt = inProgressStepData.StartedAt,
                CompletedAt = inProgressStepData.CompletedAt,
                CompletedBy = inProgressStepData.CompletedBy,
                CompletedByName = inProgressStepData.CompletedByName,
                Action = ParseWorkflowStepAction(inProgressStepData.Action),
                Comments = inProgressStepData.Comments,
                FieldValues = !string.IsNullOrEmpty(inProgressStepData.FieldValues) 
                    ? JsonSerializer.Deserialize<Dictionary<string, object?>>(inProgressStepData.FieldValues) ?? new Dictionary<string, object?>() 
                    : new Dictionary<string, object?>()
            };
            
            _logger.LogInformation("Found InProgress step: {StepId}", inProgressStep.StepId);
            return inProgressStep;
        }
        else
        {
            _logger.LogWarning("No InProgress step found for workflowInstanceId: {WorkflowInstanceId}", workflowInstanceId);
        }
        
        return null;
    }

    public async Task<List<WorkflowStepInstance>> GetStepInstancesAsync(int workflowInstanceId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = "SELECT * FROM WorkflowStepInstances WHERE WorkflowInstanceId = @WorkflowInstanceId ORDER BY ISNULL(StartedAt, '1900-01-01'), Id";
        var stepInstancesData = await connection.QueryAsync(sql, new { WorkflowInstanceId = workflowInstanceId });
        
        var stepInstances = stepInstancesData.Select(data => new WorkflowStepInstance
        {
            Id = data.Id,
            WorkflowInstanceId = data.WorkflowInstanceId,
            StepId = data.StepId,
            Status = ParseWorkflowStepInstanceStatus(data.Status),
            AssignedTo = data.AssignedTo,
            StartedAt = data.StartedAt,
            CompletedAt = data.CompletedAt,
            CompletedBy = data.CompletedBy,
            CompletedByName = data.CompletedByName,
            Action = ParseWorkflowStepAction(data.Action),
            Comments = data.Comments,
            FieldValues = !string.IsNullOrEmpty(data.FieldValues) 
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(data.FieldValues) ?? new Dictionary<string, object?>() 
                : new Dictionary<string, object?>()
        }).ToList();
        
        return stepInstances;
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
        
        // Get step definition and assigned roles
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
            return false;

        var assignedRoles = JsonSerializer.Deserialize<List<string>>(assignedRolesJson) ?? new List<string>();
        
        // Check if user has any of the assigned roles
        return assignedRoles.Any(role => userRoles.Contains(role)) || userRoles.Contains("Admin");
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

    // Overload that uses existing connection and transaction to avoid deadlocks
    private async Task<string?> GetNextStepIdAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string currentStepId, Dictionary<string, object?> formData)
    {
        // Get transitions from current step
        const string sql = @"
            SELECT wt.* FROM WorkflowTransitions wt
            INNER JOIN WorkflowInstances wi ON wt.WorkflowDefinitionId = wi.WorkflowDefinitionId
            WHERE wi.Id = @WorkflowInstanceId AND wt.FromStepId = @CurrentStepId";

        var transitions = await connection.QueryAsync<WorkflowTransition>(sql, new 
        { 
            WorkflowInstanceId = workflowInstanceId, 
            CurrentStepId = currentStepId 
        }, transaction, commandTimeout: 120);

        _logger.LogInformation("Found {Count} transitions from step '{StepId}' for workflow instance {InstanceId}", transitions.Count(), currentStepId, workflowInstanceId);

        // For now, return the first transition - in a full implementation,
        // you'd evaluate conditions here
        var nextTransition = transitions.FirstOrDefault();
        
        if (nextTransition != null)
        {
            _logger.LogInformation("Next step from '{FromStep}' is '{ToStep}'", nextTransition.FromStepId, nextTransition.ToStepId);
        }
        
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
            AND (wsi.Status IN (@Pending, @InProgress) OR wsi.Status IS NULL)";

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

        try
        {
            var instanceId = await connection.QuerySingleAsync<int>(sql, new
            {
                stepInstance.WorkflowInstanceId,
                stepInstance.StepId,
                Status = (int)stepInstance.Status,
                stepInstance.AssignedTo,
                stepInstance.StartedAt,
                FieldValues = JsonSerializer.Serialize(stepInstance.FieldValues)
            }, transaction, commandTimeout: 300);

            stepInstance.Id = instanceId;
        }
        catch (SqlException ex) when (ex.Number == -2) // Timeout
        {
            _logger.LogError(ex, "Timeout inserting step instance for workflow {WorkflowInstanceId}, step {StepId}", 
                stepInstance.WorkflowInstanceId, stepInstance.StepId);
            throw new TimeoutException($"Timeout inserting step instance for workflow {stepInstance.WorkflowInstanceId}, step {stepInstance.StepId}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting step instance for workflow {WorkflowInstanceId}, step {StepId}", 
                stepInstance.WorkflowInstanceId, stepInstance.StepId);
            throw;
        }
    }

    private async Task CreateStepInstancesBulkAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, List<WorkflowStep> steps, string currentStepId)
    {
        try
        {
            _logger.LogInformation("Creating step instances for workflow {WorkflowInstanceId}: {Steps}", 
                workflowInstanceId, string.Join(", ", steps.Select(s => s.StepId)));

            const string sql = @"
                INSERT INTO WorkflowStepInstances (WorkflowInstanceId, StepId, Status, StartedAt)
                VALUES (@WorkflowInstanceId, @StepId, @Status, @StartedAt)";

            var stepInstances = steps.Select(step => new
            {
                WorkflowInstanceId = workflowInstanceId,
                StepId = step.StepId,
                Status = (int)(step.StepId == currentStepId ? WorkflowStepInstanceStatus.InProgress : WorkflowStepInstanceStatus.Pending),
                StartedAt = step.StepId == currentStepId ? DateTime.UtcNow : (DateTime?)null
            }).ToList();

            _logger.LogInformation("Prepared {Count} step instances. Executing bulk insert...", stepInstances.Count);

            var affectedRows = await connection.ExecuteAsync(sql, stepInstances, transaction, commandTimeout: 120);
            
            _logger.LogInformation("Bulk insert completed. Affected rows: {AffectedRows}. Created {Count} step instances for workflow instance {WorkflowInstanceId}. Current step: {CurrentStepId}", 
                affectedRows, stepInstances.Count, workflowInstanceId, currentStepId);

            // Verify the inserts
            const string verifySql = "SELECT COUNT(*) FROM WorkflowStepInstances WHERE WorkflowInstanceId = @WorkflowInstanceId";
            var actualCount = await connection.QuerySingleAsync<int>(verifySql, new { WorkflowInstanceId = workflowInstanceId }, transaction);
            
            _logger.LogInformation("Verification: Found {ActualCount} step instances in database for workflow {WorkflowInstanceId}", 
                actualCount, workflowInstanceId);

            if (actualCount != stepInstances.Count)
            {
                _logger.LogError("Step instance count mismatch! Expected: {Expected}, Actual: {Actual}", 
                    stepInstances.Count, actualCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating step instances for workflow instance {WorkflowInstanceId}", workflowInstanceId);
            throw;
        }
    }

    private async Task MoveToNextStepAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string nextStepId)
    {
        // Update workflow instance current step
        const string updateWorkflowSql = "UPDATE WorkflowInstances SET CurrentStepId = @CurrentStepId WHERE Id = @Id";
        await connection.ExecuteAsync(updateWorkflowSql, new { CurrentStepId = nextStepId, Id = workflowInstanceId }, transaction);

        // Update step instance status to InProgress
        const string updateStepSql = @"
            UPDATE WorkflowStepInstances 
            SET Status = @Status, StartedAt = @StartedAt 
            WHERE WorkflowInstanceId = @WorkflowInstanceId AND StepId = @StepId";
        
        await connection.ExecuteAsync(updateStepSql, new 
        { 
            Status = WorkflowStepInstanceStatus.InProgress,
            StartedAt = DateTime.UtcNow,
            WorkflowInstanceId = workflowInstanceId,
            StepId = nextStepId
        }, transaction);

        _logger.LogInformation("Moved workflow instance {WorkflowInstanceId} to step {StepId}", workflowInstanceId, nextStepId);
    }

    private async Task CompleteWorkflowAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string completedBy)
    {
        _logger.LogInformation("CompleteWorkflowAsync called for workflow instance {WorkflowInstanceId} by {CompletedBy}", workflowInstanceId, completedBy);
        
        // Update workflow instance status
        const string sql = @"
            UPDATE WorkflowInstances 
            SET Status = @Status, CompletedAt = @CompletedAt, CompletedBy = @CompletedBy 
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new 
        { 
            Status = (int)WorkflowInstanceStatus.Completed,
            CompletedAt = DateTime.UtcNow,
            CompletedBy = completedBy,
            Id = workflowInstanceId
        }, transaction);

        _logger.LogInformation("Workflow instance {WorkflowInstanceId} status updated to Completed", workflowInstanceId);

        // Check if workflow was approved (all approval steps were approved, none rejected)
        var wasApproved = await CheckIfWorkflowWasApprovedAsync(connection, transaction, workflowInstanceId);
        
        _logger.LogInformation("Workflow {WorkflowInstanceId} approval check result: {WasApproved}", workflowInstanceId, wasApproved);
        
        if (wasApproved)
        {
            _logger.LogInformation("Calling ApproveAndApplyFormRequestAsync for workflow {WorkflowInstanceId}", workflowInstanceId);
            // Update request status to approved and apply the form request
            await ApproveAndApplyFormRequestAsync(connection, transaction, workflowInstanceId, completedBy);
        }

        _logger.LogInformation("Completed workflow instance {WorkflowInstanceId} by user {UserId}. Approved: {WasApproved}", 
            workflowInstanceId, completedBy, wasApproved);
    }

    private async Task<bool> CheckIfWorkflowWasApprovedAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId)
    {
        // Check if any approval steps were rejected
        const string sql = @"
            SELECT COUNT(*) FROM WorkflowStepInstances wsi
            INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
            INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
            WHERE wi.Id = @WorkflowInstanceId 
            AND ws.StepType = 'Approval'
            AND wsi.Action = @RejectedAction";

        var rejectedCount = await connection.QuerySingleAsync<int>(sql, new 
        { 
            WorkflowInstanceId = workflowInstanceId,
            RejectedAction = WorkflowStepAction.Rejected.ToString()
        }, transaction);

        return rejectedCount == 0;
    }

    private async Task ApproveAndApplyFormRequestAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string approvedBy)
    {
        int formRequestId = 0;
        
        try
        {
            // Get the form request ID
            const string getRequestSql = "SELECT FormRequestId FROM WorkflowInstances WHERE Id = @WorkflowInstanceId";
            formRequestId = await connection.QuerySingleAsync<int>(getRequestSql, new { WorkflowInstanceId = workflowInstanceId }, transaction);

            // Update request status to approved and set approval details
            const string updateRequestSql = @"
                UPDATE FormRequests 
                SET Status = @Status, ApprovedAt = @ApprovedAt, ApprovedBy = @ApprovedBy, ApprovedByName = @ApprovedByName
                WHERE Id = @FormRequestId";

            await connection.ExecuteAsync(updateRequestSql, new
            {
                Status = (int)RequestStatus.Approved,
                ApprovedAt = DateTime.UtcNow,
                ApprovedBy = approvedBy,
                ApprovedByName = approvedBy == "System" ? "Workflow System" : approvedBy,
                FormRequestId = formRequestId
            }, transaction);

            _logger.LogInformation("Approved form request {FormRequestId} after successful workflow completion by {ApprovedBy}", formRequestId, approvedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving form request for workflow instance {WorkflowInstanceId}", workflowInstanceId);
            throw;
        }

        // Apply the actual data changes to the target database (outside of transaction)
        try
        {
            var applicationSuccess = await ApplyFormRequestDataChangesAsync(formRequestId);
            
            if (applicationSuccess)
            {
                _logger.LogInformation("Successfully applied data changes for form request {FormRequestId}", formRequestId);
            }
            else
            {
                _logger.LogError("Failed to apply data changes for form request {FormRequestId}", formRequestId);
                // Note: We don't throw here as the workflow has already been marked complete
                // The FormRequest status will be "Approved" but not "Applied"
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying data changes for form request {FormRequestId}", formRequestId);
            // Note: We don't throw here as the workflow has already been marked complete
        }
    }

    /// <summary>
    /// Apply form request data changes to the target database
    /// This is a simplified version of FormRequestService.ApplyFormRequestAsync to avoid circular dependencies
    /// </summary>
    private async Task<bool> ApplyFormRequestDataChangesAsync(int formRequestId)
    {
        try
        {
            _logger.LogInformation("Starting data application for form request {FormRequestId}", formRequestId);
            
            // Get the form request
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            const string getRequestSql = @"
                SELECT fr.*, fd.DatabaseConnectionName, fd.TableName, fd.[Schema]
                FROM FormRequests fr 
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id 
                WHERE fr.Id = @FormRequestId AND fr.Status = @Status";

            var requestData = await connection.QuerySingleOrDefaultAsync(getRequestSql, new 
            { 
                FormRequestId = formRequestId,
                Status = (int)RequestStatus.Approved 
            }, commandTimeout: 120); // Increase timeout to 2 minutes

            if (requestData == null)
            {
                _logger.LogWarning("Form request {FormRequestId} not found or not in approved status", formRequestId);
                return false;
            }

            _logger.LogInformation("Found form request {FormRequestId} for data application", formRequestId);

            // Parse field values and original values from JSON
            var fieldValuesJson = requestData.FieldValues?.ToString() ?? "{}";
            var originalValuesJson = requestData.OriginalValues?.ToString() ?? "{}";
            
            var fieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldValuesJson) ?? new Dictionary<string, object?>();
            var originalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(originalValuesJson) ?? new Dictionary<string, object?>();

            // Convert JsonElement objects to proper types
            fieldValues = ConvertJsonElementsToValues(fieldValues);
            originalValues = ConvertJsonElementsToValues(originalValues);

            _logger.LogInformation("Converted field values for form request {FormRequestId}. Field count: {FieldCount}", 
                (object)formRequestId, (object)fieldValues.Count);

            bool success = false;
            var requestTypeValue = requestData.RequestType;
            var requestType = (RequestType)requestTypeValue;

            _logger.LogInformation("Processing request type {RequestType} for form request {FormRequestId}", 
                (object)requestType, (object)formRequestId);

            switch (requestType)
            {
                case RequestType.Insert:
                    _logger.LogInformation("Attempting INSERT for form request {FormRequestId}", (object)formRequestId);
                    try
                    {
                        success = await _dataService.InsertDataAsync(
                            requestData.DatabaseConnectionName,
                            requestData.TableName,
                            requestData.Schema,
                            fieldValues
                        );
                        _logger.LogInformation("INSERT operation completed with success: {Success} for form request {FormRequestId}", 
                            (object)success, (object)formRequestId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "INSERT operation failed for form request {FormRequestId}", (object)formRequestId);
                        throw;
                    }
                    break;
                    
                case RequestType.Update:
                    // Get primary key columns
                    var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema
                    );

                    if (!primaryKeyColumns.Any())
                    {
                        throw new InvalidOperationException($"No primary key found for table {requestData.Schema}.{requestData.TableName}");
                    }

                    // Create WHERE conditions using primary key fields from original values
                    var whereConditions = new Dictionary<string, object?>();
                    foreach (var pkColumn in primaryKeyColumns)
                    {
                        if (originalValues.ContainsKey(pkColumn))
                        {
                            whereConditions[pkColumn] = originalValues[pkColumn];
                        }
                        else
                        {
                            throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values");
                        }
                    }

                    success = await _dataService.UpdateDataAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema,
                        fieldValues,
                        whereConditions
                    );
                    break;
                    
                case RequestType.Delete:
                    // Get primary key columns
                    var deletePrimaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema
                    );

                    if (!deletePrimaryKeyColumns.Any())
                    {
                        throw new InvalidOperationException($"No primary key found for table {requestData.Schema}.{requestData.TableName}");
                    }

                    // Create WHERE conditions using primary key fields from original values
                    var deleteWhereConditions = new Dictionary<string, object?>();
                    foreach (var pkColumn in deletePrimaryKeyColumns)
                    {
                        if (originalValues.ContainsKey(pkColumn))
                        {
                            deleteWhereConditions[pkColumn] = originalValues[pkColumn];
                        }
                        else
                        {
                            throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values");
                        }
                    }

                    success = await _dataService.DeleteDataAsync(
                        requestData.DatabaseConnectionName,
                        requestData.TableName,
                        requestData.Schema,
                        deleteWhereConditions
                    );
                    break;
            }

            if (success)
            {
                // Update the request status to Applied
                const string updateStatusSql = "UPDATE FormRequests SET Status = @Status WHERE Id = @Id";
                await connection.ExecuteAsync(updateStatusSql, new { Id = formRequestId, Status = (int)RequestStatus.Applied }, commandTimeout: 120);
                
                _logger.LogInformation("Applied form request {FormRequestId} and updated status to Applied", formRequestId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying form request data changes for {FormRequestId}", formRequestId);
            return false;
        }
    }

    /// <summary>
    /// Convert JsonElement objects to their proper types for database operations
    /// </summary>
    private static Dictionary<string, object?> ConvertJsonElementsToValues(Dictionary<string, object?> values)
    {
        var converted = new Dictionary<string, object?>();
        
        foreach (var kvp in values)
        {
            if (kvp.Value is JsonElement jsonElement)
            {
                converted[kvp.Key] = ConvertJsonElementToValue(jsonElement);
            }
            else
            {
                converted[kvp.Key] = kvp.Value;
            }
        }
        
        return converted;
    }

    /// <summary>
    /// Convert a JsonElement to its appropriate .NET type
    /// </summary>
    private static object? ConvertJsonElementToValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => ConvertStringValue(element.GetString()),
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Convert a string value to its appropriate .NET type, including DateTime handling
    /// </summary>
    private static object? ConvertStringValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Try to parse as DateTime first (common serialization format)
        if (DateTime.TryParse(value, out var dateTime))
        {
            return dateTime;
        }

        // Try to parse as DateTimeOffset (ISO 8601 format)
        if (DateTimeOffset.TryParse(value, out var dateTimeOffset))
        {
            return dateTimeOffset.DateTime;
        }

        // Return as string if no special conversion needed
        return value;
    }

    #endregion

    #region Workflow Action Processing

    public async Task<WorkflowActionResult> ProcessWorkflowActionAsync(int workflowInstanceId, string actionType, string userId, string? comments = null, Dictionary<string, object?>? fieldUpdates = null)
    {
        try
        {
            var currentStep = await GetCurrentStepInstanceAsync(workflowInstanceId);
            if (currentStep == null)
            {
                return new WorkflowActionResult 
                { 
                    Success = false, 
                    Message = "No current step found for workflow instance"
                };
            }

            var action = actionType.ToLower() switch
            {
                "approve" => WorkflowStepAction.Approved,
                "reject" => WorkflowStepAction.Rejected,
                "complete" => WorkflowStepAction.Completed,
                _ => throw new ArgumentException($"Unknown action type: {actionType}")
            };

            var stepCompleted = await CompleteStepAsync(workflowInstanceId, currentStep.StepId, userId, userId, action, comments, fieldUpdates);
            
            if (stepCompleted)
            {
                // Check if workflow is now complete
                var workflowInstance = await GetWorkflowInstanceAsync(workflowInstanceId);
                var workflowCompleted = workflowInstance?.Status == WorkflowInstanceStatus.Completed || workflowInstance?.Status == WorkflowInstanceStatus.Rejected;
                var workflowApproved = workflowInstance?.Status == WorkflowInstanceStatus.Completed && action == WorkflowStepAction.Approved;
                var workflowRejected = workflowInstance?.Status == WorkflowInstanceStatus.Rejected;

                return new WorkflowActionResult
                {
                    Success = true,
                    Message = workflowRejected ? "Workflow rejected" : $"Step {action.ToString().ToLower()} successfully",
                    WorkflowCompleted = workflowCompleted,
                    WorkflowApproved = workflowApproved,
                    PreviousStepName = currentStep.StepId,
                    CurrentStepName = workflowInstance?.CurrentStepId,
                    ActorName = userId,
                    AdditionalData = workflowRejected ? new Dictionary<string, object?> { ["rejected"] = true } : new Dictionary<string, object?>()
                };
            }

            return new WorkflowActionResult { Success = false, Message = "Failed to complete step" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing workflow action {ActionType} for instance {WorkflowInstanceId}", actionType, workflowInstanceId);
            return new WorkflowActionResult 
            { 
                Success = false, 
                Message = $"Error processing action: {ex.Message}"
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
            CompletedStatus = WorkflowStepInstanceStatus.Completed
        });
        
        return stepInstances.ToList();
    }

    #endregion

    #region Workflow Progress

    public async Task<WorkflowProgress?> GetWorkflowProgressAsync(int formRequestId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        _logger.LogInformation("GetWorkflowProgressAsync called for FormRequestId: {FormRequestId}", formRequestId);
        
        const string sql = @"
            SELECT wi.Id as WorkflowInstanceId, wi.Status, wi.CurrentStepId, wi.StartedAt, wi.CompletedAt,
                   wd.Name as WorkflowName,
                   ws.Name as CurrentStepName,
                   wsi.Status as CurrentStepStatus,
                   wsi.AssignedTo as CurrentStepAssignedTo,
                   wsi.StartedAt as CurrentStepStartedAt
            FROM WorkflowInstances wi
            INNER JOIN WorkflowDefinitions wd ON wi.WorkflowDefinitionId = wd.Id
            LEFT JOIN WorkflowSteps ws ON wd.Id = ws.WorkflowDefinitionId AND ws.StepId = wi.CurrentStepId
            LEFT JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId AND wsi.StepId = wi.CurrentStepId
            WHERE wi.FormRequestId = @FormRequestId";

        var result = await connection.QueryFirstOrDefaultAsync(sql, new { FormRequestId = formRequestId });
        
        if (result == null) 
        {
            _logger.LogWarning("No workflow instance found for form request {FormRequestId}", formRequestId);
            return null;
        }

        var workflowInstanceId = (int)result.WorkflowInstanceId;
        _logger.LogInformation("Found workflow instance {WorkflowInstanceId} for FormRequestId {FormRequestId}", workflowInstanceId, formRequestId);

        // Get step counts (excluding Start/End steps for progress calculation)
        const string countSql = @"
            SELECT 
                COUNT(*) as TotalSteps,
                SUM(CASE WHEN wsi.Status = @CompletedStatus THEN 1 ELSE 0 END) as CompletedSteps
            FROM WorkflowStepInstances wsi
            INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
            INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
            WHERE wi.FormRequestId = @FormRequestId
            AND ws.StepType != @StartStepType AND ws.StepType != @EndStepType";

        var counts = await connection.QueryFirstAsync(countSql, new 
        { 
            FormRequestId = formRequestId,
            CompletedStatus = (int)WorkflowStepInstanceStatus.Completed,
            StartStepType = WorkflowStepType.Start.ToString(),
            EndStepType = WorkflowStepType.End.ToString()
        });

        _logger.LogInformation("Step counts for FormRequestId {FormRequestId}: Total={TotalSteps}, Completed={CompletedSteps}", 
            formRequestId, (int)counts.TotalSteps, (int)counts.CompletedSteps);

        // Get all step details for the workflow
        const string stepsSql = @"
            SELECT 
                ws.StepId,
                ws.Name as StepName,
                ws.Description as StepDescription,
                ws.StepType,
                ws.AssignedRoles,
                wsi.Status,
                wsi.AssignedTo,
                wsi.StartedAt,
                wsi.CompletedAt,
                wsi.CompletedBy,
                wsi.CompletedByName,
                wsi.Action,
                wsi.Comments
            FROM WorkflowStepInstances wsi
            INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
            INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
            WHERE wi.FormRequestId = @FormRequestId
            ORDER BY 
                CASE ws.StepType 
                    WHEN 'Start' THEN 1 
                    WHEN 'Approval' THEN 2 
                    WHEN 'End' THEN 3 
                    ELSE 2 
                END,
                wsi.StartedAt";

        var stepData = await connection.QueryAsync(stepsSql, new { FormRequestId = formRequestId });
        
        _logger.LogInformation("Retrieved {StepCount} step instances for FormRequestId {FormRequestId}", stepData.Count(), formRequestId);

        var steps = stepData.Select(step => new WorkflowStepProgress
        {
            StepId = step.StepId,
            StepName = step.StepName ?? step.StepId,
            StepDescription = step.StepDescription ?? "",
            StepType = ParseWorkflowStepType(step.StepType),
            Status = ParseWorkflowStepInstanceStatus(step.Status),
            AssignedTo = step.AssignedTo,
            StartedAt = step.StartedAt,
            CompletedAt = step.CompletedAt,
            CompletedBy = step.CompletedBy,
            CompletedByName = step.CompletedByName,
            Action = ParseWorkflowStepAction(step.Action),
            Comments = step.Comments,
            IsCurrent = step.StepId == (string)result.CurrentStepId,
            DaysInStep = step.StartedAt != null ? 
                (int)(DateTime.UtcNow - ((DateTime)step.StartedAt)).TotalDays : 0,
            AssignedRoles = !string.IsNullOrEmpty(step.AssignedRoles) ? 
                JsonSerializer.Deserialize<List<string>>(step.AssignedRoles) ?? new List<string>() : 
                new List<string>()
        }).ToList();

        _logger.LogInformation("Created {StepProgressCount} step progress objects for FormRequestId {FormRequestId}", steps.Count, formRequestId);
        foreach (var step in steps)
        {
            _logger.LogInformation("Step: {StepId} - {StepName} ({StepType}) - Status: {Status}", 
                step.StepId, step.StepName, step.StepType, step.Status);
        }

        var currentStepStartedAt = (DateTime?)result.CurrentStepStartedAt;
        var daysInCurrentStep = currentStepStartedAt.HasValue ? 
            (int)(DateTime.UtcNow - currentStepStartedAt.Value).TotalDays : 0;

        var progress = new WorkflowProgress
        {
            FormRequestId = formRequestId,
            WorkflowInstanceId = workflowInstanceId,
            Status = ParseWorkflowInstanceStatusFromObject(result.Status),
            WorkflowName = (string)result.WorkflowName,
            CurrentStepId = (string)result.CurrentStepId,
            CurrentStepName = (string?)result.CurrentStepName ?? result.CurrentStepId,
            CurrentStepStatus = result.CurrentStepStatus != null ? 
                ParseWorkflowStepInstanceStatusFromObject(result.CurrentStepStatus) : 
                WorkflowStepInstanceStatus.Pending,
            CurrentStepStartedAt = currentStepStartedAt,
            TotalStepsCount = (int)counts.TotalSteps,
            CompletedStepsCount = (int)counts.CompletedSteps,
            WorkflowStartedAt = (DateTime)result.StartedAt,
            WorkflowCompletedAt = (DateTime?)result.CompletedAt,
            CurrentStepAssignedTo = (string?)result.CurrentStepAssignedTo,
            Steps = steps,
            DaysInCurrentStep = daysInCurrentStep,
            IsStalled = daysInCurrentStep > 7 // Consider stalled if step is pending for more than 7 days
        };

        // Calculate progress percentage
        progress.ProgressPercentage = progress.TotalStepsCount > 0 ? 
            Math.Round((double)progress.CompletedStepsCount / progress.TotalStepsCount * 100, 1) : 0;

        _logger.LogInformation("Loaded workflow progress for form request {FormRequestId}: {StepCount} steps, {ProgressPercentage}% complete", 
            formRequestId, steps.Count, progress.ProgressPercentage);

        return progress;
    }

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

    #endregion

    private async Task<WorkflowStep?> GetWorkflowStepAsync(int workflowInstanceId, string stepId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        const string sql = @"
            SELECT ws.Id, ws.WorkflowDefinitionId, ws.StepId, ws.StepType, ws.Name, ws.Description, 
                   ws.AssignedRoles, ws.PositionX, ws.PositionY, ws.Configuration, ws.IsRequired, ws.CreatedAt
            FROM WorkflowSteps ws
            INNER JOIN WorkflowInstances wi ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId
            WHERE wi.Id = @WorkflowInstanceId AND ws.StepId = @StepId";

        var stepData = await connection.QueryFirstOrDefaultAsync(sql, new 
        { 
            WorkflowInstanceId = workflowInstanceId, 
            StepId = stepId 
        });

        if (stepData == null) return null;

        var step = new WorkflowStep
        {
            Id = stepData.Id,
            WorkflowDefinitionId = stepData.WorkflowDefinitionId,
            StepId = stepData.StepId,
            StepType = ParseWorkflowStepType(stepData.StepType),
            Name = stepData.Name,
            Description = stepData.Description ?? string.Empty,
            PositionX = stepData.PositionX,
            PositionY = stepData.PositionY,
            IsRequired = stepData.IsRequired
        };

        // Parse AssignedRoles from JSON string
        if (!string.IsNullOrEmpty(stepData.AssignedRoles))
        {
            try
            {
                step.AssignedRoles = JsonSerializer.Deserialize<List<string>>(stepData.AssignedRoles) ?? new List<string>();
            }
            catch
            {
                step.AssignedRoles = new List<string>();
            }
        }
        else
        {
            step.AssignedRoles = new List<string>();
        }

        return step;
    }

    private async Task AutoCompleteStepAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string stepId, string completedBy, string stepTypeName)
    {
        // Update step instance to completed
        const string updateStepSql = @"
            UPDATE WorkflowStepInstances 
            SET Status = @Status, CompletedAt = @CompletedAt, CompletedBy = @CompletedBy, 
                CompletedByName = @CompletedByName, Action = @Action, Comments = @Comments
            WHERE WorkflowInstanceId = @WorkflowInstanceId AND StepId = @StepId";

        await connection.ExecuteAsync(updateStepSql, new
        {
            Status = WorkflowStepInstanceStatus.Completed,
            CompletedAt = DateTime.UtcNow,
            CompletedBy = completedBy,
            CompletedByName = $"System ({stepTypeName} Step)",
            Action = WorkflowStepAction.Completed,
            Comments = $"Auto-completed {stepTypeName} step",
            WorkflowInstanceId = workflowInstanceId,
            StepId = stepId
        }, transaction);

        _logger.LogInformation("Auto-completed {StepType} step {StepId} for workflow instance {InstanceId}", 
            stepTypeName, stepId, workflowInstanceId);
    }

    private async Task HandleStepTypeAutoCompletion(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string stepId, WorkflowDefinition workflowDefinition)
    {
        var step = workflowDefinition.Steps.FirstOrDefault(s => s.StepId == stepId);
        if (step == null) 
        {
            _logger.LogWarning("Step {StepId} not found in workflow definition for auto-completion", stepId);
            return;
        }

        _logger.LogInformation("HandleStepTypeAutoCompletion called for step {StepId} of type {StepType}", stepId, step.StepType);

        if (step.StepType == WorkflowStepType.End)
        {
            _logger.LogInformation("End step '{StepId}' reached, automatically completing and finalizing workflow", stepId);
            await AutoCompleteStepAsync(connection, transaction, workflowInstanceId, stepId, "System", "End");
            await CompleteWorkflowAsync(connection, transaction, workflowInstanceId, "System");
        }
        else if (step.StepType == WorkflowStepType.Start)
        {
            _logger.LogInformation("Start step '{StepId}' reached, automatically completing", stepId);
            await AutoCompleteStepAsync(connection, transaction, workflowInstanceId, stepId, "System", "Start");
            
            var nextStepId = await GetNextStepIdAsync(connection, transaction, workflowInstanceId, stepId, new Dictionary<string, object?>());
            if (!string.IsNullOrEmpty(nextStepId))
            {
                await MoveToNextStepAsync(connection, transaction, workflowInstanceId, nextStepId);
                await HandleStepTypeAutoCompletion(connection, transaction, workflowInstanceId, nextStepId, workflowDefinition);
            }
        }
        else
        {
            _logger.LogInformation("Step {StepId} of type {StepType} does not require auto-completion", stepId, step.StepType);
        }
    }

    private async Task RejectWorkflowAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId, string rejectedBy, string? rejectionReason)
    {
        // Update workflow instance status to rejected
        const string updateWorkflowSql = @"
            UPDATE WorkflowInstances 
            SET Status = @Status, CompletedAt = @CompletedAt, CompletedBy = @CompletedBy 
            WHERE Id = @Id";

        await connection.ExecuteAsync(updateWorkflowSql, new 
        { 
            Status = (int)WorkflowInstanceStatus.Rejected,
            CompletedAt = DateTime.UtcNow,
            CompletedBy = rejectedBy,
            Id = workflowInstanceId
        }, transaction);

        // Update the associated form request status to rejected
        const string updateRequestSql = @"
            UPDATE FormRequests 
            SET Status = @Status, RejectionReason = @RejectionReason
            WHERE Id = (SELECT FormRequestId FROM WorkflowInstances WHERE Id = @WorkflowInstanceId)";

        await connection.ExecuteAsync(updateRequestSql, new
        {
            Status = (int)RequestStatus.Rejected,
            RejectionReason = rejectionReason ?? "Request rejected during workflow approval",
            WorkflowInstanceId = workflowInstanceId
        }, transaction);

        _logger.LogInformation("Rejected workflow instance {WorkflowInstanceId} by user {UserId}. Form request status updated to Rejected.", 
            workflowInstanceId, rejectedBy);
    }

    private async Task<WorkflowInstance> GetWorkflowInstanceAsync(SqlConnection connection, SqlTransaction transaction, int workflowInstanceId)
    {
        const string sql = @"
            SELECT wi.*, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as RequestFieldValues
            FROM WorkflowInstances wi
            INNER JOIN FormRequests fr ON wi.FormRequestId = fr.Id
            WHERE wi.Id = @WorkflowInstanceId";

        var result = await connection.QueryFirstOrDefaultAsync<dynamic>(sql, new { WorkflowInstanceId = workflowInstanceId }, transaction);
        
        if (result == null)
            throw new InvalidOperationException($"Workflow instance {workflowInstanceId} not found");

        return new WorkflowInstance
        {
            Id = result.Id,
            FormRequestId = result.FormRequestId,
            WorkflowDefinitionId = result.WorkflowDefinitionId,
            CurrentStepId = result.CurrentStepId,
            Status = ParseWorkflowInstanceStatusFromObject(result.Status),
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            CompletedBy = result.CompletedBy,
            FailureReason = result.FailureReason
        };
    }

    private static WorkflowInstanceStatus ParseWorkflowInstanceStatus(string statusString)
    {
        if (string.IsNullOrEmpty(statusString))
            return WorkflowInstanceStatus.InProgress;

        // Try exact match first
        if (Enum.TryParse<WorkflowInstanceStatus>(statusString, true, out var status))
            return status;

        // Handle legacy values or variations
        return statusString.ToLower() switch
        {
            "inprogress" or "in-progress" or "in_progress" => WorkflowInstanceStatus.InProgress,
            "completed" => WorkflowInstanceStatus.Completed,
            "rejected" => WorkflowInstanceStatus.Rejected,
            "cancelled" or "canceled" => WorkflowInstanceStatus.Cancelled,
            "failed" => WorkflowInstanceStatus.Failed,
            _ => WorkflowInstanceStatus.InProgress // Default fallback
        };
    }

    private static WorkflowStepInstanceStatus ParseWorkflowStepInstanceStatus(string statusString)
    {
        if (string.IsNullOrEmpty(statusString))
            return WorkflowStepInstanceStatus.Pending;

        // Try exact match first
        if (Enum.TryParse<WorkflowStepInstanceStatus>(statusString, true, out var status))
            return status;

        // Handle legacy values or variations
        return statusString.ToLower() switch
        {
            "pending" => WorkflowStepInstanceStatus.Pending,
            "inprogress" or "in-progress" or "in_progress" => WorkflowStepInstanceStatus.InProgress,
            "completed" => WorkflowStepInstanceStatus.Completed,
            "skipped" => WorkflowStepInstanceStatus.Skipped,
            "failed" => WorkflowStepInstanceStatus.Failed,
            _ => WorkflowStepInstanceStatus.Pending // Default fallback
        };
    }

    private static WorkflowStepType ParseWorkflowStepType(string stepTypeString)
    {
        if (string.IsNullOrEmpty(stepTypeString))
            return WorkflowStepType.Approval;

        // Try exact match first
        if (Enum.TryParse<WorkflowStepType>(stepTypeString, true, out var stepType))
            return stepType;

        // Handle legacy values or variations
        return stepTypeString.ToLower() switch
        {
            "start" => WorkflowStepType.Start,
            "approval" => WorkflowStepType.Approval,
            "parallel" => WorkflowStepType.Parallel,
            "branch" => WorkflowStepType.Branch,
            "end" => WorkflowStepType.End,
            _ => WorkflowStepType.Approval // Default fallback
        };
    }

    private static WorkflowStepAction? ParseWorkflowStepAction(string? actionString)
    {
        if (string.IsNullOrEmpty(actionString))
            return null;

        // Try exact match first
        if (Enum.TryParse<WorkflowStepAction>(actionString, true, out var action))
            return action;

        // Handle legacy values or variations
        return actionString.ToLower() switch
        {
            "approved" => WorkflowStepAction.Approved,
            "rejected" => WorkflowStepAction.Rejected,
            "completed" => WorkflowStepAction.Completed,
            "skipped" => WorkflowStepAction.Skipped,
            _ => null
        };
    }

    private static WorkflowInstanceStatus ParseWorkflowInstanceStatusFromObject(object statusValue)
    {
        if (statusValue == null)
            return WorkflowInstanceStatus.InProgress;

        // Handle integer values (new format)
        if (statusValue is int intValue)
        {
            if (Enum.IsDefined(typeof(WorkflowInstanceStatus), intValue))
                return (WorkflowInstanceStatus)intValue;
            return WorkflowInstanceStatus.InProgress; // Default fallback
        }

        // Handle string values (legacy format)
        if (statusValue is string stringValue)
        {
            return ParseWorkflowInstanceStatus(stringValue);
        }

        // Try to convert other types to int first, then to string
        if (int.TryParse(statusValue.ToString(), out int parsedInt))
        {
            if (Enum.IsDefined(typeof(WorkflowInstanceStatus), parsedInt))
                return (WorkflowInstanceStatus)parsedInt;
        }

        // Fallback to string parsing
        return ParseWorkflowInstanceStatus(statusValue.ToString() ?? "");
    }

    /// <summary>
    /// Safely parses WorkflowStepInstanceStatus from an object that could be either an integer or string
    /// </summary>
    private static WorkflowStepInstanceStatus ParseWorkflowStepInstanceStatusFromObject(object statusValue)
    {
        if (statusValue == null)
            return WorkflowStepInstanceStatus.Pending;

        // If it's already an integer, cast it directly
        if (statusValue is int intValue)
        {
            return (WorkflowStepInstanceStatus)intValue;
        }

        // If it's a string, try to parse it
        if (statusValue is string stringValue)
        {
            return ParseWorkflowStepInstanceStatus(stringValue);
        }

        // If it's neither, try to convert to int first, then to string as fallback
        try
        {
            var convertedInt = Convert.ToInt32(statusValue);
            return (WorkflowStepInstanceStatus)convertedInt;
        }
        catch
        {
            return ParseWorkflowStepInstanceStatus(statusValue.ToString() ?? "");
        }
    }
}
