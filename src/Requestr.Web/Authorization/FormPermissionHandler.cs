using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;

namespace Requestr.Web.Authorization;

/// <summary>
/// Authorization handler for form-specific permissions.
/// Checks if the current user has the required permission for a specific form.
/// </summary>
public class FormPermissionHandler : AuthorizationHandler<FormPermissionRequirement, FormPermissionResource>
{
    private readonly IFormPermissionService _formPermissionService;
    private readonly ILogger<FormPermissionHandler> _logger;

    public FormPermissionHandler(
        IFormPermissionService formPermissionService,
        ILogger<FormPermissionHandler> logger)
    {
        _formPermissionService = formPermissionService;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FormPermissionRequirement requirement,
        FormPermissionResource resource)
    {
        try
        {
            // Check if user is authenticated
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                _logger.LogDebug("User not authenticated for form permission check");
                return;
            }

            // Get user roles from claims
            var userRoles = GetUserRoles(context.User);
            var userId = context.User.Identity?.Name ?? "Unknown";
            
            if (!userRoles.Any())
            {
                _logger.LogDebug("No roles found for user {UserId}", userId);
                return;
            }

            // Check permission for each user role
            bool hasPermission = false;
            foreach (var role in userRoles)
            {
                var roleHasPermission = await _formPermissionService.HasPermissionAsync(
                    resource.FormDefinitionId, 
                    role, 
                    resource.PermissionType);
                    
                if (roleHasPermission)
                {
                    hasPermission = true;
                    _logger.LogDebug("User {UserId} with role {Role} has permission {Permission} for form {FormId}", 
                        userId, role, resource.PermissionType, resource.FormDefinitionId);
                    break;
                }
            }

            if (hasPermission)
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogDebug("User {UserId} denied permission {Permission} for form {FormId}. User roles: {Roles}", 
                    userId, resource.PermissionType, resource.FormDefinitionId, 
                    string.Join(", ", userRoles));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking form permission {Permission} for form {FormId} and user {UserId}", 
                resource.PermissionType, resource.FormDefinitionId, context.User.Identity?.Name);
        }
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

/// <summary>
/// Authorization handler for form permissions without requiring a specific resource.
/// Used for policy-based authorization where form ID is in the requirement.
/// </summary>
public class FormPermissionPolicyHandler : AuthorizationHandler<FormPermissionRequirement>
{
    private readonly IFormPermissionService _formPermissionService;
    private readonly ILogger<FormPermissionPolicyHandler> _logger;

    public FormPermissionPolicyHandler(
        IFormPermissionService formPermissionService,
        ILogger<FormPermissionPolicyHandler> logger)
    {
        _formPermissionService = formPermissionService;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FormPermissionRequirement requirement)
    {
        try
        {
            // This handler requires a form ID in the requirement
            if (!requirement.FormDefinitionId.HasValue)
            {
                _logger.LogDebug("No form ID provided in requirement for policy-based authorization");
                return;
            }

            // Check if user is authenticated
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                _logger.LogDebug("User not authenticated for form permission policy check");
                return;
            }

            // Get user roles from claims
            var userRoles = GetUserRoles(context.User);
            var userId = context.User.Identity?.Name ?? "Unknown";
            
            if (!userRoles.Any())
            {
                _logger.LogDebug("No roles found for user {UserId}", userId);
                return;
            }

            // Check permission for each user role
            bool hasPermission = false;
            foreach (var role in userRoles)
            {
                var roleHasPermission = await _formPermissionService.HasPermissionAsync(
                    requirement.FormDefinitionId.Value, 
                    role, 
                    requirement.PermissionType);
                    
                if (roleHasPermission)
                {
                    hasPermission = true;
                    _logger.LogDebug("User {UserId} with role {Role} has permission {Permission} for form {FormId}", 
                        userId, role, requirement.PermissionType, requirement.FormDefinitionId);
                    break;
                }
            }

            if (hasPermission)
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogDebug("User {UserId} denied permission {Permission} for form {FormId}. User roles: {Roles}", 
                    userId, requirement.PermissionType, requirement.FormDefinitionId, 
                    string.Join(", ", userRoles));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking form permission policy {Permission} for form {FormId} and user {UserId}", 
                requirement.PermissionType, requirement.FormDefinitionId, context.User.Identity?.Name);
        }
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