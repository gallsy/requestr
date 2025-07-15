namespace Requestr.Core.Models;

/// <summary>
/// Represents form-specific configuration for a workflow step
/// </summary>
public class FormWorkflowStepConfiguration : AuditableEntity
{
    public int FormDefinitionId { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public string WorkflowStepId { get; set; } = string.Empty;
    public string StepConfiguration { get; set; } = string.Empty; // JSON serialized configuration
    public string? PreviousConfiguration { get; set; } // For rollback
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public FormDefinition? FormDefinition { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }
}

/// <summary>
/// Base interface for all step configurations
/// </summary>
public interface IStepConfiguration
{
    string StepType { get; }
    bool InheritFromWorkflow { get; set; }
}

/// <summary>
/// Configuration for approval workflow steps
/// </summary>
public class ApprovalStepConfiguration : IStepConfiguration
{
    public string StepType => "approval";
    public bool InheritFromWorkflow { get; set; } = true;
    
    // Role assignment
    public string? AssignedRole { get; set; }
    
    // Field configuration
    public List<string> RequiredFields { get; set; } = new();
    public List<string> DisplayFields { get; set; } = new();
    public List<string> ReadOnlyFields { get; set; } = new();
    
    // Validation rules
    public List<FieldValidationRule> ValidationRules { get; set; } = new();
    
    // Notification settings
    public string? NotificationEmail { get; set; }
    
    // Conditional logic
    public ConditionalLogic? Conditions { get; set; }
    
    // UI settings
    public string? Instructions { get; set; }
    public int? TimeoutHours { get; set; }
}

/// <summary>
/// Configuration for parallel approval steps
/// </summary>
public class ParallelApprovalStepConfiguration : IStepConfiguration
{
    public string StepType => "parallel";
    public bool InheritFromWorkflow { get; set; } = true;
    
    public List<string> RequiredRoles { get; set; } = new();
    public bool RequireAllApprovals { get; set; } = true; // vs. any one approval
    public List<string> DisplayFields { get; set; } = new();
    public string? NotificationEmail { get; set; }
}

/// <summary>
/// Notification settings for workflow steps
/// </summary>
public class NotificationSettings
{
    public bool Email { get; set; } = true;
    public bool Teams { get; set; } = false;
    public bool InApp { get; set; } = true;
    public int? ReminderHours { get; set; } = 24;
    public string? CustomMessage { get; set; }
}

/// <summary>
/// Conditional logic for workflow steps
/// </summary>
public class ConditionalLogic
{
    public string FieldName { get; set; } = string.Empty;
    public ConditionOperator Operator { get; set; }
    public object Value { get; set; } = string.Empty;
    public ConditionAction Action { get; set; }
    public object? ActionValue { get; set; }
}

/// <summary>
/// Operators for conditional logic
/// </summary>
public enum ConditionOperator
{
    Equals = 0,
    NotEquals = 1,
    GreaterThan = 2,
    LessThan = 3,
    GreaterThanOrEqual = 4,
    LessThanOrEqual = 5,
    Contains = 6,
    NotContains = 7,
    IsEmpty = 8,
    IsNotEmpty = 9
}

/// <summary>
/// Actions for conditional logic
/// </summary>
public enum ConditionAction
{
    Skip = 0,
    RequireAdditionalApproval = 1,
    ChangeAssignedRole = 2,
    AddRequiredField = 3,
    SendAlert = 4
}

/// <summary>
/// Validation result for step configurations
/// </summary>
public class StepConfigurationValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// DTOs for API responses
/// </summary>
public class WorkflowAssignmentDto
{
    public int FormDefinitionId { get; set; }
    public int? WorkflowDefinitionId { get; set; }
    public string? WorkflowName { get; set; }
    public List<WorkflowStepConfigDto> StepConfigurations { get; set; } = new();
}

public class WorkflowStepConfigDto
{
    public string StepId { get; set; } = string.Empty;
    public string StepName { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty;
    public object Configuration { get; set; } = new();
    public bool HasConfiguration { get; set; }
}
