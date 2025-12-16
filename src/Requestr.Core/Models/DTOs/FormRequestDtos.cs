using System.ComponentModel.DataAnnotations;

namespace Requestr.Core.Models.DTOs;

public class CreateFormRequestDto
{
    [Required(ErrorMessage = "Form definition ID is required")]
    public int FormDefinitionId { get; set; }

    [Required(ErrorMessage = "Request type is required")]
    public RequestType RequestType { get; set; }

    [Required(ErrorMessage = "Field values are required")]
    public Dictionary<string, object?> FieldValues { get; set; } = new();

    public Dictionary<string, object?> OriginalValues { get; set; } = new();

    [MaxLength(1000, ErrorMessage = "Comments cannot exceed 1000 characters")]
    public string? Comments { get; set; }
}

public class UpdateFormRequestDto : CreateFormRequestDto
{
    [Required]
    public int Id { get; set; }
}

public class FormRequestSummaryDto
{
    public int Id { get; set; }
    public int FormDefinitionId { get; set; }
    public string FormName { get; set; } = string.Empty;
    public RequestType RequestType { get; set; }
    public RequestStatus Status { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Comments { get; set; }
    public bool CanApprove { get; set; }
    public bool CanEdit { get; set; }
}

public class FormRequestDetailDto : FormRequestSummaryDto
{
    public FormDefinitionSummaryDto FormDefinition { get; set; } = new();
    public Dictionary<string, object?> FieldValues { get; set; } = new();
    public Dictionary<string, object?> OriginalValues { get; set; } = new();
    public string? RejectionReason { get; set; }
    public string? AppliedRecordKey { get; set; }
    public string? FailureMessage { get; set; }
    public List<FormRequestHistoryDto> History { get; set; } = new();
}

public class BulkFormRequestDto
{
    public int Id { get; set; }
    public int FormDefinitionId { get; set; }
    public string FormName { get; set; } = string.Empty;
    public RequestType RequestType { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int SelectedRows { get; set; }
    public RequestStatus Status { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string RequestedByName { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? Comments { get; set; }
    public string? ProcessingSummary { get; set; }
    public bool CanApprove { get; set; }
    public bool CanEdit { get; set; }
    public List<FormRequestSummaryDto> FormRequests { get; set; } = new();
}

public class CreateBulkFormRequestDto
{
    [Required(ErrorMessage = "Form definition ID is required")]
    public int FormDefinitionId { get; set; }

    [Required(ErrorMessage = "Request type is required")]
    public RequestType RequestType { get; set; }

    [Required(ErrorMessage = "File name is required")]
    public string FileName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Form requests are required")]
    public List<CreateFormRequestDto> FormRequests { get; set; } = new();

    [MaxLength(1000, ErrorMessage = "Comments cannot exceed 1000 characters")]
    public string? Comments { get; set; }
}

public class SpreadsheetUploadResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<Dictionary<string, object?>> ParsedData { get; set; } = new();
    public List<SpreadsheetRowValidationResult> ValidationResults { get; set; } = new();
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
}

public class SpreadsheetRowValidationResult
{
    public int RowNumber { get; set; }
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object?> ParsedData { get; set; } = new();
}

public class FormRequestHistoryDto
{
    public int Id { get; set; }
    public int FormRequestId { get; set; }
    public FormRequestChangeType ChangeType { get; set; }
    public Dictionary<string, object?> PreviousValues { get; set; } = new();
    public Dictionary<string, object?> NewValues { get; set; } = new();
    public string ChangedBy { get; set; } = string.Empty;
    public string ChangedByName { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string? Comments { get; set; }
}

public class ApprovalDecisionDto
{
    [Required]
    public int FormRequestId { get; set; }

    [Required]
    public bool IsApproved { get; set; }

    [MaxLength(1000, ErrorMessage = "Comments cannot exceed 1000 characters")]
    public string? Comments { get; set; }
}
