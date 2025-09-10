namespace Requestr.Core.Models;

public class FormRequest : AuditableEntity
{
    public int FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }
    public RequestType RequestType { get; set; }
    public Dictionary<string, object?> FieldValues { get; set; } = new();
    public Dictionary<string, object?> OriginalValues { get; set; } = new(); // For UPDATE/DELETE
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? Comments { get; set; }
    public string? AppliedRecordKey { get; set; } // Key of the record that was inserted/updated
    public string? FailureMessage { get; set; } // Error message if application failed
    public List<FormRequestHistory> History { get; set; } = new();
    
    // Bulk request properties
    public int? BulkFormRequestId { get; set; }
    public BulkFormRequest? BulkFormRequest { get; set; }
    
    // Workflow system integration
    public int? WorkflowInstanceId { get; set; }
    public WorkflowInstance? WorkflowInstance { get; set; }
}

public class FormRequestHistory : BaseEntity
{
    public int FormRequestId { get; set; }
    public FormRequestChangeType ChangeType { get; set; }
    public Dictionary<string, object?> PreviousValues { get; set; } = new();
    public Dictionary<string, object?> NewValues { get; set; } = new();
    public string ChangedBy { get; set; } = string.Empty;
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? Comments { get; set; }
}

public class FieldChange
{
    public string FieldName { get; set; } = string.Empty;
    public object? PreviousValue { get; set; }
    public object? NewValue { get; set; }
    public string ChangeType { get; set; } = string.Empty; // FieldValue, OriginalValue
    public bool HasPreviousValue { get; set; }
    public bool HasNewValue { get; set; }
}

public class BulkFormRequest : AuditableEntity
{
    public int FormDefinitionId { get; set; }
    public FormDefinition? FormDefinition { get; set; }
    public RequestType RequestType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int SelectedRows { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public string? ApprovedByName { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? Comments { get; set; }
    public string? ProcessingSummary { get; set; } // Summary of processing results
    public int? WorkflowInstanceId { get; set; } // Link to workflow system
    public int? WorkflowFormRequestId { get; set; } // ID of the temp FormRequest created for workflow
    public List<BulkFormRequestItem> Items { get; set; } = new(); // Changed from FormRequests to Items
    public List<BulkFormRequestHistory> History { get; set; } = new();
}

public class BulkFormRequestItem : BaseEntity
{
    public int BulkFormRequestId { get; set; }
    public Dictionary<string, object?> FieldValues { get; set; } = new();
    public Dictionary<string, object?> OriginalValues { get; set; } = new(); // For updates
    public int RowNumber { get; set; } // Original row number from CSV
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string? ValidationErrors { get; set; } // JSON array of validation errors
    public string? ProcessingResult { get; set; } // Result after applying to database
}

public class BulkFormRequestHistory : BaseEntity
{
    public int BulkFormRequestId { get; set; }
    public FormRequestChangeType ChangeType { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? Comments { get; set; }
    public string? Details { get; set; } // Additional details about the change
}
