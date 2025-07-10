namespace Requestr.Core.Models;

public class WorkflowDefinition : AuditableEntity
{
    public int FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public List<WorkflowStep> Steps { get; set; } = new();
    public List<WorkflowTransition> Transitions { get; set; } = new();
}

public class WorkflowStep : BaseEntity
{
    public int WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    public string StepId { get; set; } = string.Empty; // Unique within workflow
    public WorkflowStepType StepType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> AssignedRoles { get; set; } = new(); // Entra roles
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public WorkflowStepConfiguration Configuration { get; set; } = new();
    public bool IsRequired { get; set; } = true;
    public List<WorkflowStepFieldConfiguration> FieldConfigurations { get; set; } = new();
}

public class WorkflowStepConfiguration
{
    // Configuration specific to approval steps
    public bool RequiresAllApprovers { get; set; } = false; // For parallel approvals
    public int MinimumApprovers { get; set; } = 1;
    public bool AllowReassignment { get; set; } = true;
    public bool AllowComments { get; set; } = true;
    
    // Configuration for branch steps
    public List<BranchCondition> BranchConditions { get; set; } = new();
    
    // Configuration for parallel steps
    public List<string> ParallelStepIds { get; set; } = new();
    public bool RequireAllParallelSteps { get; set; } = true;
}

public class BranchCondition
{
    public string FieldName { get; set; } = string.Empty;
    public BranchOperator Operator { get; set; }
    public object? Value { get; set; }
    public string TargetStepId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class WorkflowTransition : BaseEntity
{
    public int WorkflowDefinitionId { get; set; }
    public string FromStepId { get; set; } = string.Empty;
    public string ToStepId { get; set; } = string.Empty;
    public TransitionCondition? Condition { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TransitionCondition
{
    public string FieldName { get; set; } = string.Empty;
    public BranchOperator Operator { get; set; }
    public object? Value { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class WorkflowStepFieldConfiguration : BaseEntity
{
    public int WorkflowStepId { get; set; }
    public WorkflowStep? WorkflowStep { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool IsReadOnly { get; set; } = false;
    public bool IsRequired { get; set; } = false;
    public List<FieldValidationRule> ValidationRules { get; set; } = new();
}

public class FieldValidationRule
{
    public string RuleType { get; set; } = string.Empty; // Required, MinLength, MaxLength, Regex, etc.
    public object? Value { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class WorkflowInstance : BaseEntity
{
    public int FormRequestId { get; set; }
    public FormRequest? FormRequest { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    public string CurrentStepId { get; set; } = string.Empty;
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.InProgress;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? FailureReason { get; set; }
    public List<WorkflowStepInstance> StepInstances { get; set; } = new();
}

public class WorkflowStepInstance : BaseEntity
{
    public int WorkflowInstanceId { get; set; }
    public WorkflowInstance? WorkflowInstance { get; set; }
    public string StepId { get; set; } = string.Empty;
    public WorkflowStepInstanceStatus Status { get; set; } = WorkflowStepInstanceStatus.Pending;
    public string? AssignedTo { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CompletedBy { get; set; }
    public string? CompletedByName { get; set; }
    public WorkflowStepAction? Action { get; set; }
    public string? Comments { get; set; }
    public Dictionary<string, object?> FieldValues { get; set; } = new();
}

public class ApplicationPermission : AuditableEntity
{
    public string RoleName { get; set; } = string.Empty;
    public string Permission { get; set; } = string.Empty;
    public int? ResourceId { get; set; } // Optional: specific form or resource ID
    public bool IsGranted { get; set; } = true;
}

public class WorkflowActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool WorkflowCompleted { get; set; }
    public bool WorkflowApproved { get; set; }
    public string? PreviousStepName { get; set; }
    public string? CurrentStepName { get; set; }
    public string? ActorName { get; set; }
    public Dictionary<string, object?> AdditionalData { get; set; } = new();
}
