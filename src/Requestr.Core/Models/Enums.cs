namespace Requestr.Core.Models;

public enum RequestStatus
{
    Pending,
    Approved,
    Rejected,
    Applied,
    Failed  // Added for when application to target database fails
}

public enum FormRequestChangeType
{
    Created,
    Updated,
    StatusChanged,
    Approved,
    Rejected,
    Applied,
    Failed  // Added for when application to target database fails
}

public enum RequestType
{
    Insert,
    Update,
    Delete
}

public enum FieldDataType
{
    String,
    Integer,
    Decimal,
    Boolean,
    Date,
    DateTime,
    Time,
    Text,
    Email,
    Phone,
    Url
}

public enum AuthorizationResult
{
    Authorized,
    Unauthorized,
    Forbidden
}

// Workflow system enums
public enum WorkflowStepType
{
    Start,
    Approval,
    Parallel,
    Branch,
    End
}

public enum WorkflowInstanceStatus
{
    InProgress,
    Completed,
    Cancelled,
    Failed
}

public enum WorkflowStepInstanceStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped,
    Failed
}

public enum WorkflowStepAction
{
    Approved,
    Rejected,
    Completed,
    Skipped
}

public enum BranchOperator
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    NotContains,
    StartsWith,
    EndsWith,
    IsEmpty,
    IsNotEmpty
}
