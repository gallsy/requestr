using Microsoft.AspNetCore.Authorization;
using Requestr.Core.Models;

namespace Requestr.Web.Authorization;

/// <summary>
/// Extension methods for authorization policy building
/// </summary>
public static class AuthorizationPolicyExtensions
{
    /// <summary>
    /// Add a policy that requires a specific form permission
    /// </summary>
    public static AuthorizationPolicyBuilder RequireFormPermission(this AuthorizationPolicyBuilder builder, FormPermissionType permission, int? formId = null)
    {
        return builder.AddRequirements(new FormPermissionRequirement(permission, formId));
    }

    /// <summary>
    /// Add a policy that requires any of the specified form permissions
    /// </summary>
    public static AuthorizationPolicyBuilder RequireAnyFormPermission(this AuthorizationPolicyBuilder builder, int? formId = null, params FormPermissionType[] permissions)
    {
        foreach (var permission in permissions)
        {
            builder.AddRequirements(new FormPermissionRequirement(permission, formId));
        }
        return builder;
    }

    /// <summary>
    /// Helper method to create form-specific policy names
    /// </summary>
    public static string GetFormPolicyName(int formId, FormPermissionType permission)
    {
        return $"Form_{formId}_{permission}";
    }

    /// <summary>
    /// Helper method to create form-specific policy names for multiple permissions
    /// </summary>
    public static string GetFormPolicyName(int formId, params FormPermissionType[] permissions)
    {
        return $"Form_{formId}_{string.Join("_", permissions)}";
    }
}

/// <summary>
/// Service for creating dynamic authorization policies for forms
/// </summary>
public interface IDynamicAuthorizationPolicyProvider
{
    /// <summary>
    /// Create a policy for a specific form and permission
    /// </summary>
    AuthorizationPolicy CreateFormPolicy(int formId, FormPermissionType permission);
    
    /// <summary>
    /// Create a policy that requires any of the specified permissions for a form
    /// </summary>
    AuthorizationPolicy CreateFormPolicyAny(int formId, params FormPermissionType[] permissions);
    
    /// <summary>
    /// Create a policy that requires all of the specified permissions for a form
    /// </summary>
    AuthorizationPolicy CreateFormPolicyAll(int formId, params FormPermissionType[] permissions);
}

public class DynamicAuthorizationPolicyProvider : IDynamicAuthorizationPolicyProvider
{
    public AuthorizationPolicy CreateFormPolicy(int formId, FormPermissionType permission)
    {
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireFormPermission(permission, formId)
            .Build();
    }

    public AuthorizationPolicy CreateFormPolicyAny(int formId, params FormPermissionType[] permissions)
    {
        var builder = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser();

        // Add all permissions as separate requirements - the handler will succeed if ANY are met
        foreach (var permission in permissions)
        {
            builder.AddRequirements(new FormPermissionRequirement(permission, formId));
        }

        return builder.Build();
    }

    public AuthorizationPolicy CreateFormPolicyAll(int formId, params FormPermissionType[] permissions)
    {
        var builder = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser();

        // Add all permissions as separate requirements - ALL must be met
        foreach (var permission in permissions)
        {
            builder.AddRequirements(new FormPermissionRequirement(permission, formId));
        }

        return builder.Build();
    }
}
