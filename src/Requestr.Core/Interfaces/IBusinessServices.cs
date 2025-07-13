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

public interface IFormPermissionService
{
    // Get permissions for a specific form
    Task<List<FormPermission>> GetFormPermissionsAsync(int formDefinitionId);
    
    // Get permissions for a specific role on a form
    Task<List<FormPermission>> GetFormPermissionsByRoleAsync(int formDefinitionId, string roleName);
    
    // Check if a role has a specific permission on a form
    Task<bool> HasPermissionAsync(int formDefinitionId, string roleName, FormPermissionType permissionType);
    
    // Check if a user has a specific permission on a form (checks all their roles)
    Task<bool> UserHasPermissionAsync(int formDefinitionId, string userId, FormPermissionType permissionType);
    
    // Grant or revoke permissions
    Task<Result> SetPermissionAsync(int formDefinitionId, string roleName, FormPermissionType permissionType, bool isGranted, string updatedBy);
    
    // Set multiple permissions at once
    Task<Result> SetMultiplePermissionsAsync(int formDefinitionId, Dictionary<string, Dictionary<FormPermissionType, bool>> rolePermissions, string updatedBy);
    
    // Get all available Entra ID roles for permission assignment
    Task<List<string>> GetAvailableRolesAsync();
    
    // Get roles that have permissions on a specific form
    Task<List<string>> GetFormRolesAsync(int formDefinitionId);
    
    // Add a new role to a form (with default permissions)
    Task<Result> AddRoleToFormAsync(int formDefinitionId, string roleName, string createdBy);
    
    // Remove a role from a form (deletes all its permissions)
    Task<Result> RemoveRoleFromFormAsync(int formDefinitionId, string roleName);
    
    // Initialize default permissions for a new form
    Task<Result> InitializeDefaultPermissionsAsync(int formDefinitionId, string createdBy);
    
    // Delete all permissions for a form (used when form is deleted)
    Task<Result> DeleteFormPermissionsAsync(int formDefinitionId);
}
