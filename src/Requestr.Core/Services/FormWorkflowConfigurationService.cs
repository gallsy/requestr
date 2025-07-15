using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using System.Text.Json;

namespace Requestr.Core.Services;

public class FormWorkflowConfigurationService : IFormWorkflowConfigurationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FormWorkflowConfigurationService> _logger;
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly IWorkflowService _workflowService;
    private readonly string _connectionString;
    
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FormWorkflowConfigurationService(
        IConfiguration configuration,
        ILogger<FormWorkflowConfigurationService> logger,
        IFormDefinitionService formDefinitionService,
        IWorkflowService workflowService)
    {
        _configuration = configuration;
        _logger = logger;
        _formDefinitionService = formDefinitionService;
        _workflowService = workflowService;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
    }

    public async Task<WorkflowAssignmentDto> AssignWorkflowToFormAsync(int formDefinitionId, int workflowDefinitionId, string assignedBy)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            // Update form definition with workflow assignment
            var sql = @"
                UPDATE FormDefinitions 
                SET WorkflowDefinitionId = @WorkflowDefinitionId, 
                    WorkflowAssignedAt = @AssignedAt,
                    WorkflowAssignedBy = @AssignedBy,
                    UpdatedAt = @AssignedAt, 
                    UpdatedBy = @AssignedBy
                WHERE Id = @FormDefinitionId";

            await connection.ExecuteAsync(sql, new
            {
                FormDefinitionId = formDefinitionId,
                WorkflowDefinitionId = workflowDefinitionId,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = assignedBy
            }, transaction);

            // Create default configurations for all workflow steps
            await CreateDefaultConfigurationsAsync(formDefinitionId, workflowDefinitionId, assignedBy, connection, transaction);

            await transaction.CommitAsync();

            _logger.LogInformation("Assigned workflow {WorkflowId} to form {FormId} by {User}", 
                workflowDefinitionId, formDefinitionId, assignedBy);

            return await GetFormWorkflowConfigurationAsync(formDefinitionId) 
                ?? throw new InvalidOperationException("Failed to retrieve workflow configuration after assignment");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning workflow {WorkflowId} to form {FormId}", 
                workflowDefinitionId, formDefinitionId);
            throw;
        }
    }

    public async Task RemoveWorkflowFromFormAsync(int formDefinitionId, string removedBy)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            // Remove workflow assignment from form
            var sql = @"
                UPDATE FormDefinitions 
                SET WorkflowDefinitionId = NULL, 
                    WorkflowAssignedAt = NULL,
                    WorkflowAssignedBy = NULL,
                    UpdatedAt = @RemovedAt, 
                    UpdatedBy = @RemovedBy
                WHERE Id = @FormDefinitionId";

            await connection.ExecuteAsync(sql, new
            {
                FormDefinitionId = formDefinitionId,
                RemovedAt = DateTime.UtcNow,
                RemovedBy = removedBy
            }, transaction);

            // Mark all step configurations as inactive
            var configSql = @"
                UPDATE FormWorkflowStepConfigurations 
                SET IsActive = 0, UpdatedAt = @RemovedAt, UpdatedBy = @RemovedBy
                WHERE FormDefinitionId = @FormDefinitionId";

            await connection.ExecuteAsync(configSql, new
            {
                FormDefinitionId = formDefinitionId,
                RemovedAt = DateTime.UtcNow,
                RemovedBy = removedBy
            }, transaction);

            await transaction.CommitAsync();

            _logger.LogInformation("Removed workflow from form {FormId} by {User}", formDefinitionId, removedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing workflow from form {FormId}", formDefinitionId);
            throw;
        }
    }

    public async Task<WorkflowAssignmentDto?> GetFormWorkflowConfigurationAsync(int formDefinitionId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get form with workflow assignment
            var formSql = @"
                SELECT fd.Id, fd.WorkflowDefinitionId, wd.Name as WorkflowName
                FROM FormDefinitions fd
                LEFT JOIN WorkflowDefinitions wd ON fd.WorkflowDefinitionId = wd.Id
                WHERE fd.Id = @FormDefinitionId";

            var formData = await connection.QueryFirstOrDefaultAsync(formSql, new { FormDefinitionId = formDefinitionId });

            if (formData?.WorkflowDefinitionId == null)
            {
                return new WorkflowAssignmentDto
                {
                    FormDefinitionId = formDefinitionId,
                    WorkflowDefinitionId = null,
                    WorkflowName = null,
                    StepConfigurations = new()
                };
            }

            // Get workflow steps with configurations
            var stepsSql = @"
                SELECT ws.StepId, ws.Name as StepName, ws.StepType,
                       fwsc.StepConfiguration, fwsc.Id as ConfigId
                FROM WorkflowSteps ws
                LEFT JOIN FormWorkflowStepConfigurations fwsc ON ws.StepId = fwsc.WorkflowStepId 
                    AND fwsc.FormDefinitionId = @FormDefinitionId 
                    AND fwsc.WorkflowDefinitionId = @WorkflowDefinitionId
                    AND fwsc.IsActive = 1
                WHERE ws.WorkflowDefinitionId = @WorkflowDefinitionId
                ORDER BY ws.PositionY, ws.PositionX";

            var steps = await connection.QueryAsync(stepsSql, new
            {
                FormDefinitionId = formDefinitionId,
                WorkflowDefinitionId = formData.WorkflowDefinitionId
            });

            var stepConfigs = new List<WorkflowStepConfigDto>();
            foreach (var step in steps)
            {
                var config = new WorkflowStepConfigDto
                {
                    StepId = step.StepId,
                    StepName = step.StepName,
                    StepType = GetStepTypeName((int)step.StepType),
                    HasConfiguration = !string.IsNullOrEmpty(step.StepConfiguration)
                };

                if (config.HasConfiguration)
                {
                    config.Configuration = DeserializeConfiguration(step.StepConfiguration, config.StepType);
                }

                stepConfigs.Add(config);
            }

            return new WorkflowAssignmentDto
            {
                FormDefinitionId = formDefinitionId,
                WorkflowDefinitionId = formData.WorkflowDefinitionId,
                WorkflowName = formData.WorkflowName,
                StepConfigurations = stepConfigs
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting workflow configuration for form {FormId}", formDefinitionId);
            throw;
        }
    }

    public async Task UpdateStepConfigurationAsync(int formDefinitionId, string workflowStepId, IStepConfiguration configuration, string updatedBy)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var configJson = JsonSerializer.Serialize(configuration, _jsonOptions);

            // Get current configuration for rollback
            var currentConfigSql = @"
                SELECT StepConfiguration 
                FROM FormWorkflowStepConfigurations 
                WHERE FormDefinitionId = @FormDefinitionId AND WorkflowStepId = @WorkflowStepId AND IsActive = 1";

            var currentConfig = await connection.QueryFirstOrDefaultAsync<string>(currentConfigSql, new
            {
                FormDefinitionId = formDefinitionId,
                WorkflowStepId = workflowStepId
            });

            // Update or insert configuration
            var sql = @"
                MERGE FormWorkflowStepConfigurations AS target
                USING (SELECT @FormDefinitionId as FormDefinitionId, @WorkflowStepId as WorkflowStepId) AS source
                ON target.FormDefinitionId = source.FormDefinitionId AND target.WorkflowStepId = source.WorkflowStepId AND target.IsActive = 1
                WHEN MATCHED THEN
                    UPDATE SET 
                        StepConfiguration = @StepConfiguration,
                        PreviousConfiguration = @PreviousConfiguration,
                        UpdatedAt = @UpdatedAt,
                        UpdatedBy = @UpdatedBy
                WHEN NOT MATCHED THEN
                    INSERT (FormDefinitionId, WorkflowDefinitionId, WorkflowStepId, StepConfiguration, IsActive, CreatedAt, CreatedBy)
                    VALUES (@FormDefinitionId, (SELECT WorkflowDefinitionId FROM FormDefinitions WHERE Id = @FormDefinitionId), 
                            @WorkflowStepId, @StepConfiguration, 1, @UpdatedAt, @UpdatedBy);";

            await connection.ExecuteAsync(sql, new
            {
                FormDefinitionId = formDefinitionId,
                WorkflowStepId = workflowStepId,
                StepConfiguration = configJson,
                PreviousConfiguration = currentConfig,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = updatedBy
            });

            _logger.LogInformation("Updated step configuration for form {FormId}, step {StepId} by {User}", 
                formDefinitionId, workflowStepId, updatedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating step configuration for form {FormId}, step {StepId}", 
                formDefinitionId, workflowStepId);
            throw;
        }
    }

    public async Task<T?> GetStepConfigurationAsync<T>(int formDefinitionId, string workflowStepId) where T : class, IStepConfiguration
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT StepConfiguration 
                FROM FormWorkflowStepConfigurations 
                WHERE FormDefinitionId = @FormDefinitionId AND WorkflowStepId = @WorkflowStepId AND IsActive = 1";

            var configJson = await connection.QueryFirstOrDefaultAsync<string>(sql, new
            {
                FormDefinitionId = formDefinitionId,
                WorkflowStepId = workflowStepId
            });

            if (string.IsNullOrEmpty(configJson))
                return null;

            return JsonSerializer.Deserialize<T>(configJson, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting step configuration for form {FormId}, step {StepId}", 
                formDefinitionId, workflowStepId);
            throw;
        }
    }

    public async Task<StepConfigurationValidationResult> ValidateStepConfigurationAsync(int formDefinitionId, string workflowStepId, IStepConfiguration configuration)
    {
        var result = new StepConfigurationValidationResult { IsValid = true };

        try
        {
            // Get form definition to validate against
            var form = await _formDefinitionService.GetByIdAsync(formDefinitionId);
            if (form == null)
            {
                result.IsValid = false;
                result.Errors.Add("Form definition not found");
                return result;
            }

            // Validate based on configuration type
            if (configuration is ApprovalStepConfiguration approvalConfig)
            {
                ValidateApprovalConfiguration(approvalConfig, form, result);
            }
            else if (configuration is ParallelApprovalStepConfiguration parallelConfig)
            {
                ValidateParallelApprovalConfiguration(parallelConfig, form, result);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating step configuration for form {FormId}, step {StepId}", 
                formDefinitionId, workflowStepId);
            result.IsValid = false;
            result.Errors.Add($"Validation error: {ex.Message}");
            return result;
        }
    }

    public async Task<List<FormDefinition>> GetFormsUsingWorkflowAsync(int workflowDefinitionId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT Id, Name, Description, WorkflowDefinitionId
                FROM FormDefinitions 
                WHERE WorkflowDefinitionId = @WorkflowDefinitionId AND IsActive = 1";

            var forms = await connection.QueryAsync(sql, new { WorkflowDefinitionId = workflowDefinitionId });

            return forms.Select(f => new FormDefinition
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                WorkflowDefinitionId = f.WorkflowDefinitionId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting forms using workflow {WorkflowId}", workflowDefinitionId);
            throw;
        }
    }

    public async Task<bool> RollbackStepConfigurationAsync(int formDefinitionId, string workflowStepId, string rolledBackBy)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                UPDATE FormWorkflowStepConfigurations 
                SET StepConfiguration = PreviousConfiguration,
                    PreviousConfiguration = StepConfiguration,
                    UpdatedAt = @UpdatedAt,
                    UpdatedBy = @UpdatedBy
                WHERE FormDefinitionId = @FormDefinitionId 
                AND WorkflowStepId = @WorkflowStepId 
                AND IsActive = 1 
                AND PreviousConfiguration IS NOT NULL";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                FormDefinitionId = formDefinitionId,
                WorkflowStepId = workflowStepId,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = rolledBackBy
            });

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Rolled back step configuration for form {FormId}, step {StepId} by {User}", 
                    formDefinitionId, workflowStepId, rolledBackBy);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rolling back step configuration for form {FormId}, step {StepId}", 
                formDefinitionId, workflowStepId);
            throw;
        }
    }

    public async Task CreateDefaultConfigurationsAsync(int formDefinitionId, int workflowDefinitionId, string createdBy)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        await CreateDefaultConfigurationsAsync(formDefinitionId, workflowDefinitionId, createdBy, connection, transaction);
        await transaction.CommitAsync();
    }

    private async Task CreateDefaultConfigurationsAsync(int formDefinitionId, int workflowDefinitionId, string createdBy, 
        SqlConnection connection, SqlTransaction transaction)
    {
        // Get form definition for smart defaults
        var form = await _formDefinitionService.GetByIdAsync(formDefinitionId);
        if (form == null) return;

        // Get workflow steps
        var stepsSql = @"
            SELECT StepId, StepType, Name, AssignedRoles
            FROM WorkflowSteps 
            WHERE WorkflowDefinitionId = @WorkflowDefinitionId";

        var steps = await connection.QueryAsync(stepsSql, new { WorkflowDefinitionId = workflowDefinitionId }, transaction);

        foreach (var step in steps)
        {
            var config = CreateDefaultConfigurationForStep(step, form);
            if (config != null)
            {
                var configJson = JsonSerializer.Serialize(config, _jsonOptions);

                var insertSql = @"
                    INSERT INTO FormWorkflowStepConfigurations 
                    (FormDefinitionId, WorkflowDefinitionId, WorkflowStepId, StepConfiguration, IsActive, CreatedAt, CreatedBy)
                    VALUES (@FormDefinitionId, @WorkflowDefinitionId, @WorkflowStepId, @StepConfiguration, 1, @CreatedAt, @CreatedBy)";

                await connection.ExecuteAsync(insertSql, new
                {
                    FormDefinitionId = formDefinitionId,
                    WorkflowDefinitionId = workflowDefinitionId,
                    WorkflowStepId = step.StepId,
                    StepConfiguration = configJson,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                }, transaction);
            }
        }
    }

    private IStepConfiguration? CreateDefaultConfigurationForStep(dynamic step, FormDefinition form)
    {
        var stepType = (int)step.StepType;
        
        return stepType switch
        {
            2 => CreateDefaultApprovalConfiguration(step, form), // Approval
            3 => CreateDefaultParallelApprovalConfiguration(step, form), // Parallel
            _ => null
        };
    }

    private ApprovalStepConfiguration CreateDefaultApprovalConfiguration(dynamic step, FormDefinition form)
    {
        return new ApprovalStepConfiguration
        {
            AssignedRole = GetDefaultRole(step.AssignedRoles) ?? "Admin",
            RequiredFields = AutoDetectCriticalFields(form.Fields),
            DisplayFields = form.Fields.Where(f => f.IsVisibleInDataView).Select(f => f.Name).ToList(),
            ValidationRules = new(),
            NotificationEmail = "", // Empty by default, can be configured later
            Instructions = $"Please review and approve this {form.Name.ToLower()} request."
        };
    }

    private ParallelApprovalStepConfiguration CreateDefaultParallelApprovalConfiguration(dynamic step, FormDefinition form)
    {
        var roles = GetRolesFromJson(step.AssignedRoles);
        return new ParallelApprovalStepConfiguration
        {
            RequiredRoles = roles,
            RequireAllApprovals = true,
            DisplayFields = form.Fields.Where(f => f.IsVisibleInDataView).Select(f => f.Name).ToList(),
            NotificationEmail = "" // Empty by default, can be configured later
        };
    }

    private List<string> AutoDetectCriticalFields(List<FormField> fields)
    {
        return fields.Where(f =>
            f.IsRequired ||
            f.Name.Contains("Name", StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains("Status", StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains("Amount", StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains("Code", StringComparison.OrdinalIgnoreCase)
        ).Select(f => f.Name).ToList();
    }

    private string? GetDefaultRole(string? assignedRolesJson)
    {
        if (string.IsNullOrEmpty(assignedRolesJson))
            return null;

        try
        {
            var roles = JsonSerializer.Deserialize<List<string>>(assignedRolesJson);
            return roles?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private List<string> GetRolesFromJson(string? assignedRolesJson)
    {
        if (string.IsNullOrEmpty(assignedRolesJson))
            return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(assignedRolesJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private string GetStepTypeName(int stepType)
    {
        return stepType switch
        {
            0 => "start",
            1 => "end",
            2 => "approval",
            3 => "parallel",
            4 => "branch",
            _ => "unknown"
        };
    }

    private object DeserializeConfiguration(string configJson, string stepType)
    {
        try
        {
            return stepType switch
            {
                "approval" => JsonSerializer.Deserialize<ApprovalStepConfiguration>(configJson, _jsonOptions) ?? new ApprovalStepConfiguration(),
                "parallel" => JsonSerializer.Deserialize<ParallelApprovalStepConfiguration>(configJson, _jsonOptions) ?? new ParallelApprovalStepConfiguration(),
                _ => JsonSerializer.Deserialize<object>(configJson, _jsonOptions) ?? new object()
            };
        }
        catch
        {
            return new object();
        }
    }

    private void ValidateApprovalConfiguration(ApprovalStepConfiguration config, FormDefinition form, StepConfigurationValidationResult result)
    {
        // Validate fields exist
        var invalidFields = config.RequiredFields
            .Concat(config.DisplayFields)
            .Concat(config.ReadOnlyFields)
            .Where(fieldName => !form.Fields.Any(f => f.Name == fieldName))
            .Distinct()
            .ToList();

        if (invalidFields.Any())
        {
            result.IsValid = false;
            result.Errors.Add($"Fields not found in form: {string.Join(", ", invalidFields)}");
        }

        // Validate validation rules
        foreach (var rule in config.ValidationRules)
        {
            if (!form.Fields.Any(f => f.Name == rule.FieldName))
            {
                result.IsValid = false;
                result.Errors.Add($"Validation rule references non-existent field: {rule.FieldName}");
            }
        }

        // Validate conditional logic
        if (config.Conditions != null && !form.Fields.Any(f => f.Name == config.Conditions.FieldName))
        {
            result.IsValid = false;
            result.Errors.Add($"Conditional logic references non-existent field: {config.Conditions.FieldName}");
        }

        // Warnings for best practices
        if (string.IsNullOrEmpty(config.AssignedRole))
        {
            result.Warnings.Add("No role assigned to approval step");
        }

        if (!config.RequiredFields.Any())
        {
            result.Warnings.Add("No fields marked as required for approval");
        }
    }

    private void ValidateParallelApprovalConfiguration(ParallelApprovalStepConfiguration config, FormDefinition form, StepConfigurationValidationResult result)
    {
        // Validate fields exist
        var invalidFields = config.DisplayFields
            .Where(fieldName => !form.Fields.Any(f => f.Name == fieldName))
            .ToList();

        if (invalidFields.Any())
        {
            result.IsValid = false;
            result.Errors.Add($"Display fields not found in form: {string.Join(", ", invalidFields)}");
        }

        // Validate roles
        if (!config.RequiredRoles.Any())
        {
            result.IsValid = false;
            result.Errors.Add("Parallel approval step must have at least one required role");
        }
    }
}
