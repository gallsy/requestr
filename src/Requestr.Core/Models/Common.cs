namespace Requestr.Core.Models;

public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public T? Value { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<string> Errors { get; private set; } = new();

    private Result(bool isSuccess, T? value, string? errorMessage, List<string>? errors = null)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        Errors = errors ?? new List<string>();
    }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string errorMessage) => new(false, default, errorMessage);
    public static Result<T> Failure(List<string> errors) => new(false, default, null, errors);
    public static Result<T> Failure(string errorMessage, List<string> errors) => new(false, default, errorMessage, errors);
}

public class Result
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<string> Errors { get; private set; } = new();

    private Result(bool isSuccess, string? errorMessage, List<string>? errors = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Errors = errors ?? new List<string>();
    }

    public static Result Success() => new(true, null);
    public static Result Failure(string errorMessage) => new(false, errorMessage);
    public static Result Failure(List<string> errors) => new(false, null, errors);
    public static Result Failure(string errorMessage, List<string> errors) => new(false, errorMessage, errors);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult Failure(string error) => new() { IsValid = false, Errors = [error] };
    public static ValidationResult Failure(List<string> errors) => new() { IsValid = false, Errors = errors };
}

public enum EmailProvider
{
    SMTP = 0,
    SendGrid = 1
}

public enum EmailMode
{
    Production = 0,
    Test = 1  // Test mode - emails are logged but not sent
}

public class EmailConfiguration
{
    public int Id { get; set; }
    public EmailProvider Provider { get; set; } = EmailProvider.SMTP;
    public EmailMode Mode { get; set; } = EmailMode.Production;
    public bool IsEnabled { get; set; } = false;
    public string? FromEmail { get; set; }
    public string? FromName { get; set; }
    
    // SMTP Settings
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    
    // SendGrid Settings
    public string? SendGridApiKey { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class NotificationTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class NotificationVariable
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public static class NotificationTemplateKeys
{
    public const string NewRequestCreated = "NEW_REQUEST_CREATED";
    public const string RequestApproved = "REQUEST_APPROVED";
    public const string RequestRejected = "REQUEST_REJECTED";
    public const string WorkflowStepPending = "WORKFLOW_STEP_PENDING";
    public const string WorkflowStepApproved = "WORKFLOW_STEP_APPROVED";
    public const string WorkflowStepRejected = "WORKFLOW_STEP_REJECTED";
    public const string WorkflowStepCompleted = "WORKFLOW_STEP_COMPLETED";
    public const string FormSubmissionComplete = "FORM_SUBMISSION_COMPLETE";
}

public static class NotificationVariables
{
    public static readonly List<NotificationVariable> AllVariables = new()
    {
        // Request Variables
        new() { Key = "{{RequestId}}", DisplayName = "Request ID", Description = "The unique identifier of the request", Category = "Request" },
        new() { Key = "{{RequestDescription}}", DisplayName = "Request Description", Description = "The description of the request", Category = "Request" },
        new() { Key = "{{RequestComments}}", DisplayName = "Request Comments", Description = "Comments added by the user when creating the request", Category = "Request" },
        new() { Key = "{{RequestStatus}}", DisplayName = "Request Status", Description = "Current status of the request", Category = "Request" },
        new() { Key = "{{RequestCreatedDate}}", DisplayName = "Request Created Date", Description = "When the request was created", Category = "Request" },
        
        // Form Variables
        new() { Key = "{{FormName}}", DisplayName = "Form Name", Description = "Name of the form", Category = "Form" },
        new() { Key = "{{FormDescription}}", DisplayName = "Form Description", Description = "Description of the form", Category = "Form" },
        
        // User Variables
        new() { Key = "{{CreatingUser}}", DisplayName = "Creating User", Description = "The user who created the request", Category = "User" },
        new() { Key = "{{CreatingUserEmail}}", DisplayName = "Creating User Email", Description = "Email of the user who created the request", Category = "User" },
        new() { Key = "{{ApproverName}}", DisplayName = "Approver Name", Description = "Name of the approver", Category = "User" },
        new() { Key = "{{ApproverEmail}}", DisplayName = "Approver Email", Description = "Email of the approver", Category = "User" },
        
        // Workflow Variables
        new() { Key = "{{WorkflowStepName}}", DisplayName = "Workflow Step Name", Description = "Name of the current workflow step", Category = "Workflow" },
        new() { Key = "{{WorkflowName}}", DisplayName = "Workflow Name", Description = "Name of the workflow", Category = "Workflow" },
        
        // System Variables
        new() { Key = "{{SystemUrl}}", DisplayName = "System URL", Description = "Base URL of the system", Category = "System" },
        new() { Key = "{{RequestUrl}}", DisplayName = "Request URL", Description = "Direct link to the request", Category = "System" },
        new() { Key = "{{SystemName}}", DisplayName = "System Name", Description = "Name of the system", Category = "System" }
    };
    
    public static List<NotificationVariable> GetVariablesByCategory(string category)
    {
        return AllVariables.Where(v => v.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
