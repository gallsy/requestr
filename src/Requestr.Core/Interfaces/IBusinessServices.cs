using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

// Legacy alias for IAdvancedNotificationService - use IAdvancedNotificationService directly instead
public interface INotificationService : IAdvancedNotificationService
{
    // This interface now extends IAdvancedNotificationService, so all methods are inherited
    // No additional methods needed - this is just for backwards compatibility
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
