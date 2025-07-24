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
    Created = 0,
    Updated = 1,
    StatusChanged = 2,
    Approved = 3,
    Rejected = 4,
    Applied = 5,
    Failed = 6,  // Added for when application to target database fails
    WorkflowStarted = 7,
    WorkflowStepCompleted = 8,
    WorkflowStepApproved = 9,
    WorkflowStepRejected = 10,
    WorkflowCompleted = 11
}

public enum RequestType
{
    Insert = 0,
    Update = 1,
    Delete = 2
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
    Start = 0,
    End = 1,
    Approval = 2,
    Parallel = 3,
    Branch = 4
}

public enum WorkflowInstanceStatus
{
    InProgress = 0,
    Completed = 1,
    Cancelled = 2,
    Failed = 3
}

public enum WorkflowStepInstanceStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Skipped = 3,
    Failed = 4
}

public enum WorkflowStepAction
{
    None = 0,
    Approved = 1,
    Rejected = 2,
    Completed = 3
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

public enum FormDisplayMode
{
    Edit,           // Full editing capability (current FormSubmission behavior)
    ReadOnly,       // All fields visible but read-only
    Approval        // Fields configured per workflow step field configurations
}
