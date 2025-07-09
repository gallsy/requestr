using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;

namespace Requestr.Core.Interfaces;

public interface IValidationService
{
    ValidationResult ValidateFormDefinition(FormDefinition formDefinition);
    ValidationResult ValidateFormRequest(FormRequest formRequest);
    ValidationResult ValidateFieldValue(FormField field, object? value);
    ValidationResult ValidateBusinessRules(FormRequest formRequest);
}

public interface IAuthorizationService
{
    Task<bool> CanUserAccessFormAsync(string userId, int formDefinitionId);
    Task<bool> CanUserApproveRequestAsync(string userId, int formRequestId);
    Task<bool> CanUserEditFormAsync(string userId, int formDefinitionId);
    Task<List<string>> GetUserRolesAsync(string userId);
    Task<bool> HasRoleAsync(string userId, string role);
}

public interface INotificationService
{
    Task SendApprovalRequestAsync(FormRequest formRequest, List<string> approverEmails);
    Task SendApprovalDecisionAsync(FormRequest formRequest, string requesterEmail);
    Task SendRequestStatusUpdateAsync(FormRequest formRequest, string requesterEmail);
}

public interface IAuditService
{
    Task LogActivityAsync(string userId, string action, string details, int? relatedEntityId = null);
    Task<List<AuditLog>> GetAuditLogsAsync(int? entityId = null, string? userId = null, DateTime? fromDate = null);
}

public class AuditLog
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public int? RelatedEntityId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? AdditionalData { get; set; }
}
