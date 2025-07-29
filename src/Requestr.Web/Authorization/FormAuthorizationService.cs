using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Utilities;

namespace Requestr.Web.Authorization;

/// <summary>
/// Service for checking form permissions in application code
/// </summary>
public interface IFormAuthorizationService
{
    /// <summary>
    /// Check if the current user has a specific permission for a form
    /// </summary>
    Task<bool> UserHasPermissionAsync(ClaimsPrincipal user, int formDefinitionId, FormPermissionType permission);
    
    /// <summary>
    /// Check if the current user has any of the specified permissions for a form
    /// </summary>
    Task<bool> UserHasAnyPermissionAsync(ClaimsPrincipal user, int formDefinitionId, params FormPermissionType[] permissions);
    
    /// <summary>
    /// Check if the current user has all of the specified permissions for a form
    /// </summary>
    Task<bool> UserHasAllPermissionsAsync(ClaimsPrincipal user, int formDefinitionId, params FormPermissionType[] permissions);
    
    /// <summary>
    /// Get all permissions the current user has for a form
    /// </summary>
    Task<List<FormPermissionType>> GetUserPermissionsAsync(ClaimsPrincipal user, int formDefinitionId);
    

}

public class FormAuthorizationService : IFormAuthorizationService
{
    private readonly IFormPermissionService _formPermissionService;
    private readonly ILogger<FormAuthorizationService> _logger;

    public FormAuthorizationService(
        IFormPermissionService formPermissionService,
        ILogger<FormAuthorizationService> logger)
    {
        _formPermissionService = formPermissionService;
        _logger = logger;
    }

    public async Task<bool> UserHasPermissionAsync(ClaimsPrincipal user, int formDefinitionId, FormPermissionType permission)
    {
        try
        {
            if (!user.Identity?.IsAuthenticated ?? true)
                return false;

            var userRoles = ClaimsHelper.GetUserRoles(user);
            
            foreach (var role in userRoles)
            {
                var hasPermission = await _formPermissionService.HasPermissionAsync(formDefinitionId, role, permission);
                if (hasPermission)
                    return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking permission {Permission} for user {UserId} on form {FormId}", 
                permission, user.Identity?.Name, formDefinitionId);
            return false;
        }
    }

    public async Task<bool> UserHasAnyPermissionAsync(ClaimsPrincipal user, int formDefinitionId, params FormPermissionType[] permissions)
    {
        foreach (var permission in permissions)
        {
            if (await UserHasPermissionAsync(user, formDefinitionId, permission))
                return true;
        }
        return false;
    }

    public async Task<bool> UserHasAllPermissionsAsync(ClaimsPrincipal user, int formDefinitionId, params FormPermissionType[] permissions)
    {
        foreach (var permission in permissions)
        {
            if (!await UserHasPermissionAsync(user, formDefinitionId, permission))
                return false;
        }
        return true;
    }

    public async Task<List<FormPermissionType>> GetUserPermissionsAsync(ClaimsPrincipal user, int formDefinitionId)
    {
        var permissions = new HashSet<FormPermissionType>();
        
        try
        {
            if (!user.Identity?.IsAuthenticated ?? true)
                return new List<FormPermissionType>();

            var userRoles = ClaimsHelper.GetUserRoles(user);
            
            foreach (var role in userRoles)
            {
                var rolePermissions = await _formPermissionService.GetFormPermissionsByRoleAsync(formDefinitionId, role);
                foreach (var rolePermission in rolePermissions.Where(p => p.IsGranted))
                {
                    permissions.Add(rolePermission.PermissionType);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting permissions for user {UserId} on form {FormId}", 
                user.Identity?.Name, formDefinitionId);
        }
        
        return permissions.ToList();
    }
}
