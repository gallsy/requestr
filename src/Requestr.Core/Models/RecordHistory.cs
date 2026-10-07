namespace Requestr.Core.Models;

/// <summary>A request (or bulk request row) that targeted a specific record.</summary>
public sealed class RecordHistoryEntry
{
    public int? FormRequestId { get; set; }
    public int? BulkFormRequestId { get; set; }
    public int? BulkRowNumber { get; set; }
    public int FormDefinitionId { get; set; }
    public string FormName { get; set; } = string.Empty;
    public RequestType RequestType { get; set; }
    public RequestStatus Status { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public List<RecordFieldChange> Changes { get; set; } = new();

    public bool IsOutstanding => Status is RequestStatus.Pending or RequestStatus.Approved;
}

public sealed record RecordFieldChange(string FieldName, object? OldValue, object? NewValue);
