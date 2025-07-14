namespace Requestr.Core.Models.DTOs;

public class WorkflowDefinitionDto
{
    public int Id { get; set; }
    public int FormDefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public List<WorkflowStepDto> Steps { get; set; } = new();
    public List<WorkflowTransitionDto> Transitions { get; set; } = new();
}

public class WorkflowStepDto
{
    public int Id { get; set; }
    public string StepId { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> AssignedRoles { get; set; } = new();
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public WorkflowStepConfigurationDto Configuration { get; set; } = new();
    public bool IsRequired { get; set; }
    public List<WorkflowStepFieldConfigurationDto> FieldConfigurations { get; set; } = new();
}

public class WorkflowStepConfigurationDto
{
    public bool RequiresAllApprovers { get; set; }
    public int MinimumApprovers { get; set; } = 1;
    public bool AllowReassignment { get; set; } = true;
    public bool AllowComments { get; set; } = true;
    public List<BranchConditionDto> BranchConditions { get; set; } = new();
    public List<string> ParallelStepIds { get; set; } = new();
    public bool RequireAllParallelSteps { get; set; } = true;
}

public class BranchConditionDto
{
    public string FieldName { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string TargetStepId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class WorkflowTransitionDto
{
    public int Id { get; set; }
    public string FromStepId { get; set; } = string.Empty;
    public string ToStepId { get; set; } = string.Empty;
    public TransitionConditionDto? Condition { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TransitionConditionDto
{
    public string FieldName { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class WorkflowStepFieldConfigurationDto
{
    public int Id { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool IsReadOnly { get; set; } = false;
    public bool IsRequired { get; set; } = false;
    public List<FieldValidationRuleDto> ValidationRules { get; set; } = new();
}

public class FieldValidationRuleDto
{
    public string RuleType { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class WorkflowInstanceDto
{
    public int Id { get; set; }
    public int FormRequestId { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public string CurrentStepId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? FailureReason { get; set; }
    public List<WorkflowStepInstanceDto> StepInstances { get; set; } = new();
    
    // Additional properties for display
    public string FormName { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
}

public class WorkflowStepInstanceDto
{
    public int Id { get; set; }
    public string StepId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AssignedTo { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? CompletedByName { get; set; }
    public string? Action { get; set; }
    public string? Comments { get; set; }
    public Dictionary<string, object?> FieldValues { get; set; } = new();
    
    // Additional properties for display
    public string StepName { get; set; } = string.Empty;
    public string StepType { get; set; } = string.Empty;
    public List<string> AssignedRoles { get; set; } = new();
}

public class WorkflowStepActionRequest
{
    public int WorkflowInstanceId { get; set; }
    public string StepId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // "Approved", "Rejected", "Completed"
    public string? Comments { get; set; }
    public Dictionary<string, object?> FieldValues { get; set; } = new();
}

public class CreateWorkflowRequest
{
    public int FormDefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class WorkflowValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
