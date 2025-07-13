using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;

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
    
    /// <summary>
    /// Check authorization using the ASP.NET Core authorization service
    /// </summary>
    Task<bool> AuthorizeAsync(ClaimsPrincipal user, int formDefinitionId, FormPermissionType permission);
}

public class FormAuthorizationService : IFormAuthorizationService
{
    private readonly IFormPermissionService _formPermissionService;
    private readonly Microsoft.AspNetCore.Authorization.IAuthorizationService _authorizationService;
    private readonly ILogger<FormAuthorizationService> _logger;

    public FormAuthorizationService(
        IFormPermissionService formPermissionService,
        Microsoft.AspNetCore.Authorization.IAuthorizationService authorizationService,
        ILogger<FormAuthorizationService> logger)
    {
        _formPermissionService = formPermissionService;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    public async Task<bool> UserHasPermissionAsync(ClaimsPrincipal user, int formDefinitionId, FormPermissionType permission)
    {
        try
        {
            if (!user.Identity?.IsAuthenticated ?? true)
                return false;

            var userRoles = GetUserRoles(user);
            
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

            var userRoles = GetUserRoles(user);
            
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

    public async Task<bool> AuthorizeAsync(ClaimsPrincipal user, int formDefinitionId, FormPermissionType permission)
    {
        var resource = new FormPermissionResource(formDefinitionId, permission);
        var requirement = new FormPermissionRequirement(permission);
        
        var result = await _authorizationService.AuthorizeAsync(user, resource, requirement);
        return result.Succeeded;
    }

    /// <summary>
    /// Extract user roles from claims. Works with Entra ID app roles.
    /// </summary>
    private static List<string> GetUserRoles(ClaimsPrincipal user)
    {
        var roles = new List<string>();

        // Get roles from standard role claims
        roles.AddRange(user.FindAll(ClaimTypes.Role).Select(c => c.Value));
        
        // Get roles from Entra ID app role claims (these come in as "roles" claim type)
        roles.AddRange(user.FindAll("roles").Select(c => c.Value));
        
        // Also check for "role" claim (singular)
        roles.AddRange(user.FindAll("role").Select(c => c.Value));

        return roles.Distinct().ToList();
    }
}
