using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Requestr.Core.Models;
using Requestr.Web.Authorization;

namespace Requestr.Web.Authorization;

/// <summary>
/// Custom authorization attribute for form-specific permissions
/// </summary>
public class FormPermissionAttribute : Attribute, IAuthorizationFilter
{
    public FormPermissionType Permission { get; }
    public string? FormIdParameter { get; }

    public FormPermissionAttribute(FormPermissionType permission, string formIdParameter = "formId")
    {
        Permission = permission;
        FormIdParameter = formIdParameter;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Check if user is authenticated
        if (!context.HttpContext.User.Identity?.IsAuthenticated ?? true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        // Extract form ID from route parameters, query string, or form data
        var formId = GetFormId(context);
        if (!formId.HasValue)
        {
            context.Result = new ForbidResult("Form ID not found in request");
            return;
        }

        // Create the authorization requirement and resource
        var requirement = new FormPermissionRequirement(Permission);
        var resource = new FormPermissionResource(formId.Value, Permission);

        // Get the authorization service
        var authorizationService = context.HttpContext.RequestServices
            .GetService<Microsoft.AspNetCore.Authorization.IAuthorizationService>();

        if (authorizationService == null)
        {
            context.Result = new StatusCodeResult(500);
            return;
        }

        // Check authorization asynchronously
        var authTask = authorizationService.AuthorizeAsync(context.HttpContext.User, resource, requirement);
        authTask.Wait(); // Note: This is not ideal but necessary for synchronous interface

        if (!authTask.Result.Succeeded)
        {
            context.Result = new ForbidResult();
        }
    }

    private int? GetFormId(AuthorizationFilterContext context)
    {
        // Try route values first
        if (context.RouteData.Values.TryGetValue(FormIdParameter ?? "formId", out var routeValue))
        {
            if (int.TryParse(routeValue?.ToString(), out var routeFormId))
                return routeFormId;
        }

        // Try query string
        if (context.HttpContext.Request.Query.TryGetValue(FormIdParameter ?? "formId", out var queryValue))
        {
            if (int.TryParse(queryValue.FirstOrDefault(), out var queryFormId))
                return queryFormId;
        }

        // Try form data (for POST requests)
        if (context.HttpContext.Request.HasFormContentType)
        {
            if (context.HttpContext.Request.Form.TryGetValue(FormIdParameter ?? "formId", out var formValue))
            {
                if (int.TryParse(formValue.FirstOrDefault(), out var formFormId))
                    return formFormId;
            }
        }

        return null;
    }
}

/// <summary>
/// Authorize attribute that requires any of the specified form permissions
/// </summary>
public class RequireAnyFormPermissionAttribute : FormPermissionAttribute
{
    public FormPermissionType[] Permissions { get; }

    public RequireAnyFormPermissionAttribute(params FormPermissionType[] permissions) 
        : base(permissions.FirstOrDefault())
    {
        Permissions = permissions;
    }

    public new void OnAuthorization(AuthorizationFilterContext context)
    {
        // Check if user is authenticated
        if (!context.HttpContext.User.Identity?.IsAuthenticated ?? true)
        {
            context.Result = new ChallengeResult();
            return;
        }

        // Extract form ID
        var formId = GetFormId(context);
        if (!formId.HasValue)
        {
            context.Result = new ForbidResult("Form ID not found in request");
            return;
        }

        // Get the form authorization service
        var formAuthService = context.HttpContext.RequestServices
            .GetService<IFormAuthorizationService>();

        if (formAuthService == null)
        {
            context.Result = new StatusCodeResult(500);
            return;
        }

        // Check if user has any of the required permissions
        var authTask = formAuthService.UserHasAnyPermissionAsync(context.HttpContext.User, formId.Value, Permissions);
        authTask.Wait();

        if (!authTask.Result)
        {
            context.Result = new ForbidResult();
        }
    }

    private int? GetFormId(AuthorizationFilterContext context)
    {
        // Try route values first
        if (context.RouteData.Values.TryGetValue(FormIdParameter ?? "formId", out var routeValue))
        {
            if (int.TryParse(routeValue?.ToString(), out var routeFormId))
                return routeFormId;
        }

        // Try query string
        if (context.HttpContext.Request.Query.TryGetValue(FormIdParameter ?? "formId", out var queryValue))
        {
            if (int.TryParse(queryValue.FirstOrDefault(), out var queryFormId))
                return queryFormId;
        }

        // Try form data (for POST requests)
        if (context.HttpContext.Request.HasFormContentType)
        {
            if (context.HttpContext.Request.Form.TryGetValue(FormIdParameter ?? "formId", out var formValue))
            {
                if (int.TryParse(formValue.FirstOrDefault(), out var formFormId))
                    return formFormId;
            }
        }

        return null;
    }
}
