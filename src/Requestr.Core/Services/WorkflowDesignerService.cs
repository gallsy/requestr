using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using System.Text.Json;

namespace Requestr.Core.Services;

public class WorkflowDesignerService : IWorkflowDesignerService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkflowDesignerService> _logger;
    private readonly IWorkflowService _workflowService;
    private readonly string _connectionString;

    public WorkflowDesignerService(
        IConfiguration configuration, 
        ILogger<WorkflowDesignerService> logger,
        IWorkflowService workflowService)
    {
        _configuration = configuration;
        _logger = logger;
        _workflowService = workflowService;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
    }

    #region Workflow Design Operations

    public async Task<WorkflowDefinition> CreateEmptyWorkflowAsync(int formDefinitionId, string name, string description)
    {
        try
        {
            _logger.LogInformation("Creating empty workflow for form {FormDefinitionId} with name '{Name}'", formDefinitionId, name);

            // Validate inputs
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Workflow name cannot be empty", nameof(name));
            }

            // Check if form definition exists (only if a form ID is provided)
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            if (formDefinitionId > 0)
            {
                var formExists = await connection.QuerySingleOrDefaultAsync<int?>(
                    "SELECT Id FROM FormDefinitions WHERE Id = @FormDefinitionId",
                    new { FormDefinitionId = formDefinitionId });

                if (formExists == null)
                {
                    throw new InvalidOperationException($"Form definition with ID {formDefinitionId} does not exist");
                }
            }

            var workflowDefinition = new WorkflowDefinition
            {
                FormDefinitionId = formDefinitionId > 0 ? formDefinitionId : 0, // Will be NULL in database if 0
                Name = name,
                Description = description,
                Version = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System", // TODO: Get from current user context
                Steps = new List<WorkflowStep>
                {
                    // Create default start and end steps
                    new WorkflowStep
                    {
                        StepId = "start",
                        StepType = WorkflowStepType.Start,
                        Name = "Start",
                        Description = "Workflow start point",
                        PositionX = 100,
                        PositionY = 100,
                        IsRequired = true,
                        FieldConfigurations = new List<WorkflowStepFieldConfiguration>()
                    },
                    new WorkflowStep
                    {
                        StepId = "end",
                        StepType = WorkflowStepType.End,
                        Name = "End",
                        Description = "Workflow end point",
                        PositionX = 500,
                        PositionY = 100,
                        IsRequired = true,
                        FieldConfigurations = new List<WorkflowStepFieldConfiguration>()
                    }
                },
                Transitions = new List<WorkflowTransition>
                {
                    new WorkflowTransition
                    {
                        FromStepId = "start",
                        ToStepId = "end",
                        Name = "Direct to End"
                    }
                }
            };

            _logger.LogInformation("Creating workflow definition through WorkflowService");
            var result = await _workflowService.CreateWorkflowDefinitionAsync(workflowDefinition);
            
            _logger.LogInformation("Successfully created workflow definition with ID {WorkflowId}", result.Id);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating empty workflow for form {FormDefinitionId} with name '{Name}'", formDefinitionId, name);
            throw;
        }
    }

    public async Task<WorkflowStep> AddStepAsync(int workflowDefinitionId, WorkflowStepType stepType, string name, int positionX, int positionY)
    {
        using var connection = new SqlConnection(_connectionString);

        // Generate unique step ID
        var stepId = await GenerateUniqueStepIdAsync(connection, workflowDefinitionId, stepType);

        var step = new WorkflowStep
        {
            WorkflowDefinitionId = workflowDefinitionId,
            StepId = stepId,
            StepType = stepType,
            Name = name,
            Description = $"{stepType} step",
            PositionX = positionX,
            PositionY = positionY,
            IsRequired = stepType != WorkflowStepType.Branch, // Branch steps are optional
            AssignedRoles = new List<string>(),
            Configuration = CreateDefaultStepConfiguration(stepType),
            FieldConfigurations = new List<WorkflowStepFieldConfiguration>()
        };

        const string sql = @"
            INSERT INTO WorkflowSteps (WorkflowDefinitionId, StepId, StepType, Name, Description, AssignedRoles, PositionX, PositionY, Configuration, IsRequired, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@WorkflowDefinitionId, @StepId, @StepType, @Name, @Description, @AssignedRoles, @PositionX, @PositionY, @Configuration, @IsRequired, @CreatedAt)";

        var id = await connection.QuerySingleAsync<int>(sql, new
        {
            step.WorkflowDefinitionId,
            step.StepId,
            StepType = (int)step.StepType,
            step.Name,
            step.Description,
            AssignedRoles = JsonSerializer.Serialize(step.AssignedRoles),
            step.PositionX,
            step.PositionY,
            Configuration = JsonSerializer.Serialize(step.Configuration),
            step.IsRequired,
            CreatedAt = DateTime.UtcNow
        });

        step.Id = id;

        _logger.LogInformation("Added step {StepId} of type {StepType} to workflow {WorkflowId}", stepId, stepType, workflowDefinitionId);

        return step;
    }

    public async Task<WorkflowStep> UpdateStepAsync(int stepId, WorkflowStep stepData)
    {
        using var connection = new SqlConnection(_connectionString);

        const string sql = @"
            UPDATE WorkflowSteps 
            SET Name = @Name, Description = @Description, AssignedRoles = @AssignedRoles, 
                PositionX = @PositionX, PositionY = @PositionY, Configuration = @Configuration, IsRequired = @IsRequired
            WHERE Id = @Id";

        await connection.ExecuteAsync(sql, new
        {
            stepData.Name,
            stepData.Description,
            AssignedRoles = JsonSerializer.Serialize(stepData.AssignedRoles),
            stepData.PositionX,
            stepData.PositionY,
            Configuration = JsonSerializer.Serialize(stepData.Configuration),
            stepData.IsRequired,
            Id = stepId
        });

        _logger.LogInformation("Updated step {StepId}", stepId);

        return stepData;
    }

    public async Task<bool> DeleteStepAsync(int stepId)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Get basic step info before deletion (without AssignedRoles to avoid JSON parsing issues)
            var stepInfo = await connection.QueryFirstOrDefaultAsync<(int WorkflowDefinitionId, string StepId, WorkflowStepType StepType)>(
                "SELECT WorkflowDefinitionId, StepId, StepType FROM WorkflowSteps WHERE Id = @Id", 
                new { Id = stepId }, 
                transaction);

            if (stepInfo.WorkflowDefinitionId == 0) return false;

            // Don't allow deletion of start or end steps
            if (stepInfo.StepType == WorkflowStepType.Start || stepInfo.StepType == WorkflowStepType.End)
            {
                throw new InvalidOperationException("Cannot delete start or end steps");
            }

            // Delete field configurations
            await connection.ExecuteAsync(
                "DELETE FROM WorkflowStepFieldConfigurations WHERE WorkflowStepId = @StepId", 
                new { StepId = stepId }, 
                transaction);

            // Delete transitions involving this step
            await connection.ExecuteAsync(
                "DELETE FROM WorkflowTransitions WHERE WorkflowDefinitionId = @WorkflowDefinitionId AND (FromStepId = @StepId OR ToStepId = @StepId)", 
                new { stepInfo.WorkflowDefinitionId, StepId = stepInfo.StepId }, 
                transaction);

            // Delete the step
            var rowsAffected = await connection.ExecuteAsync(
                "DELETE FROM WorkflowSteps WHERE Id = @Id", 
                new { Id = stepId }, 
                transaction);

            transaction.Commit();

            _logger.LogInformation("Deleted step {StepId} from workflow {WorkflowId}", stepInfo.StepId, stepInfo.WorkflowDefinitionId);

            return rowsAffected > 0;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<WorkflowTransition> AddTransitionAsync(int workflowDefinitionId, string fromStepId, string toStepId, string? name = null)
    {
        using var connection = new SqlConnection(_connectionString);

        var transition = new WorkflowTransition
        {
            WorkflowDefinitionId = workflowDefinitionId,
            FromStepId = fromStepId,
            ToStepId = toStepId,
            Name = name ?? $"{fromStepId} → {toStepId}"
        };

        const string sql = @"
            INSERT INTO WorkflowTransitions (WorkflowDefinitionId, FromStepId, ToStepId, Name, CreatedAt)
            OUTPUT INSERTED.Id
            VALUES (@WorkflowDefinitionId, @FromStepId, @ToStepId, @Name, @CreatedAt)";

        var id = await connection.QuerySingleAsync<int>(sql, new
        {
            transition.WorkflowDefinitionId,
            transition.FromStepId,
            transition.ToStepId,
            transition.Name,
            CreatedAt = DateTime.UtcNow
        });

        transition.Id = id;

        _logger.LogInformation("Added transition from {FromStep} to {ToStep} in workflow {WorkflowId}", fromStepId, toStepId, workflowDefinitionId);

        return transition;
    }

    public async Task<bool> DeleteTransitionAsync(int transitionId)
    {
        using var connection = new SqlConnection(_connectionString);

        const string sql = "DELETE FROM WorkflowTransitions WHERE Id = @Id";
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = transitionId });

        _logger.LogInformation("Deleted transition {TransitionId}", transitionId);

        return rowsAffected > 0;
    }

    #endregion

    #region Step Configuration

    public async Task<WorkflowStepFieldConfiguration> ConfigureStepFieldAsync(int workflowStepId, string fieldName, bool isVisible, bool isReadOnly, bool isRequired)
    {
        using var connection = new SqlConnection(_connectionString);

        // Check if configuration already exists
        const string checkSql = "SELECT Id FROM WorkflowStepFieldConfigurations WHERE WorkflowStepId = @WorkflowStepId AND FieldName = @FieldName";
        var existingId = await connection.QueryFirstOrDefaultAsync<int?>(checkSql, new { WorkflowStepId = workflowStepId, FieldName = fieldName });

        if (existingId.HasValue)
        {
            // Update existing configuration
            const string updateSql = @"
                UPDATE WorkflowStepFieldConfigurations 
                SET IsVisible = @IsVisible, IsReadOnly = @IsReadOnly, IsRequired = @IsRequired
                WHERE Id = @Id";

            await connection.ExecuteAsync(updateSql, new
            {
                IsVisible = isVisible,
                IsReadOnly = isReadOnly,
                IsRequired = isRequired,
                Id = existingId.Value
            });

            return new WorkflowStepFieldConfiguration
            {
                Id = existingId.Value,
                WorkflowStepId = workflowStepId,
                FieldName = fieldName,
                IsVisible = isVisible,
                IsReadOnly = isReadOnly,
                IsRequired = isRequired,
                ValidationRules = new List<FieldValidationRule>()
            };
        }
        else
        {
            // Create new configuration
            var configuration = new WorkflowStepFieldConfiguration
            {
                WorkflowStepId = workflowStepId,
                FieldName = fieldName,
                IsVisible = isVisible,
                IsReadOnly = isReadOnly,
                IsRequired = isRequired,
                ValidationRules = new List<FieldValidationRule>()
            };

            const string insertSql = @"
                INSERT INTO WorkflowStepFieldConfigurations (WorkflowStepId, FieldName, IsVisible, IsReadOnly, IsRequired, ValidationRules, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@WorkflowStepId, @FieldName, @IsVisible, @IsReadOnly, @IsRequired, @ValidationRules, @CreatedAt)";

            var id = await connection.QuerySingleAsync<int>(insertSql, new
            {
                configuration.WorkflowStepId,
                configuration.FieldName,
                configuration.IsVisible,
                configuration.IsReadOnly,
                configuration.IsRequired,
                ValidationRules = JsonSerializer.Serialize(configuration.ValidationRules),
                CreatedAt = DateTime.UtcNow
            });

            configuration.Id = id;

            _logger.LogInformation("Configured field {FieldName} for step {StepId}", fieldName, workflowStepId);

            return configuration;
        }
    }

    public async Task<bool> DeleteStepFieldConfigurationAsync(int configurationId)
    {
        using var connection = new SqlConnection(_connectionString);

        const string sql = "DELETE FROM WorkflowStepFieldConfigurations WHERE Id = @Id";
        var rowsAffected = await connection.ExecuteAsync(sql, new { Id = configurationId });

        return rowsAffected > 0;
    }

    public async Task<List<WorkflowStepFieldConfiguration>> GetStepFieldConfigurationsAsync(int workflowStepId)
    {
        using var connection = new SqlConnection(_connectionString);

        const string sql = "SELECT * FROM WorkflowStepFieldConfigurations WHERE WorkflowStepId = @WorkflowStepId";
        var configurations = await connection.QueryAsync<WorkflowStepFieldConfiguration>(sql, new { WorkflowStepId = workflowStepId });

        return configurations.ToList();
    }

    #endregion

    #region Validation

    public async Task<List<string>> ValidateWorkflowAsync(int workflowDefinitionId)
    {
        var errors = new List<string>();
        var workflow = await _workflowService.GetWorkflowDefinitionAsync(workflowDefinitionId);

        if (workflow == null)
        {
            errors.Add("Workflow definition not found");
            return errors;
        }

        // Check for start step
        var startSteps = workflow.Steps.Where(s => s.StepType == WorkflowStepType.Start).ToList();
        if (!startSteps.Any())
        {
            errors.Add("Workflow must have exactly one start step");
        }
        else if (startSteps.Count > 1)
        {
            errors.Add("Workflow can only have one start step");
        }

        // Check for end step
        var endSteps = workflow.Steps.Where(s => s.StepType == WorkflowStepType.End).ToList();
        if (!endSteps.Any())
        {
            errors.Add("Workflow must have at least one end step");
        }

        // Check for orphaned steps (no path to/from start/end)
        var reachableSteps = GetReachableSteps(workflow, startSteps.FirstOrDefault()?.StepId);
        var unreachableSteps = workflow.Steps.Where(s => !reachableSteps.Contains(s.StepId) && s.StepType != WorkflowStepType.Start).ToList();
        
        foreach (var step in unreachableSteps)
        {
            errors.Add($"Step '{step.Name}' ({step.StepId}) is not reachable from the start step");
        }

        // Check approval steps have assigned roles
        var approvalSteps = workflow.Steps.Where(s => s.StepType == WorkflowStepType.Approval).ToList();
        foreach (var step in approvalSteps)
        {
            if (!step.AssignedRoles.Any())
            {
                errors.Add($"Approval step '{step.Name}' ({step.StepId}) must have at least one assigned role");
            }
        }

        // Check branch steps have conditions
        var branchSteps = workflow.Steps.Where(s => s.StepType == WorkflowStepType.Branch).ToList();
        foreach (var step in branchSteps)
        {
            var outgoingTransitions = workflow.Transitions.Where(t => t.FromStepId == step.StepId).ToList();
            if (outgoingTransitions.Count < 2)
            {
                errors.Add($"Branch step '{step.Name}' ({step.StepId}) must have at least two outgoing transitions");
            }
        }

        return errors;
    }

    public async Task<bool> IsWorkflowValidAsync(int workflowDefinitionId)
    {
        var errors = await ValidateWorkflowAsync(workflowDefinitionId);
        return !errors.Any();
    }

    #endregion

    #region Private Helper Methods

    private async Task<string> GenerateUniqueStepIdAsync(SqlConnection connection, int workflowDefinitionId, WorkflowStepType stepType)
    {
        var baseId = stepType.ToString().ToLower();
        var counter = 1;
        string stepId;

        do
        {
            stepId = $"{baseId}{counter}";
            var exists = await connection.QueryFirstOrDefaultAsync<bool>(
                "SELECT CASE WHEN EXISTS(SELECT 1 FROM WorkflowSteps WHERE WorkflowDefinitionId = @WorkflowDefinitionId AND StepId = @StepId) THEN 1 ELSE 0 END",
                new { WorkflowDefinitionId = workflowDefinitionId, StepId = stepId }
            );

            if (!exists) break;
            counter++;
        } 
        while (true);

        return stepId;
    }

    private WorkflowStepConfiguration CreateDefaultStepConfiguration(WorkflowStepType stepType)
    {
        return stepType switch
        {
            WorkflowStepType.Approval => new WorkflowStepConfiguration
            {
                RequiresAllApprovers = false,
                MinimumApprovers = 1,
                AllowReassignment = true,
                AllowComments = true
            },
            WorkflowStepType.Parallel => new WorkflowStepConfiguration
            {
                RequireAllParallelSteps = true,
                ParallelStepIds = new List<string>()
            },
            WorkflowStepType.Branch => new WorkflowStepConfiguration
            {
                BranchConditions = new List<BranchCondition>()
            },
            _ => new WorkflowStepConfiguration()
        };
    }

    private HashSet<string> GetReachableSteps(WorkflowDefinition workflow, string? startStepId)
    {
        var reachable = new HashSet<string>();
        if (string.IsNullOrEmpty(startStepId)) return reachable;

        var queue = new Queue<string>();
        queue.Enqueue(startStepId);
        reachable.Add(startStepId);

        while (queue.Count > 0)
        {
            var currentStepId = queue.Dequeue();
            var outgoingTransitions = workflow.Transitions.Where(t => t.FromStepId == currentStepId);

            foreach (var transition in outgoingTransitions)
            {
                if (!reachable.Contains(transition.ToStepId))
                {
                    reachable.Add(transition.ToStepId);
                    queue.Enqueue(transition.ToStepId);
                }
            }
        }

        return reachable;
    }

    #endregion
}
