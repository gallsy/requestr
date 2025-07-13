using Microsoft.AspNetCore.Authorization;
using Requestr.Core.Models;

namespace Requestr.Web.Authorization;

/// <summary>
/// Authorization requirement for form-specific permissions
/// </summary>
public class FormPermissionRequirement : IAuthorizationRequirement
{
    public FormPermissionType PermissionType { get; }
    public int? FormDefinitionId { get; }

    public FormPermissionRequirement(FormPermissionType permissionType, int? formDefinitionId = null)
    {
        PermissionType = permissionType;
        FormDefinitionId = formDefinitionId;
    }
}

/// <summary>
/// Resource class to pass form context to authorization handler
/// </summary>
public class FormPermissionResource
{
    public int FormDefinitionId { get; set; }
    public FormPermissionType PermissionType { get; set; }

    public FormPermissionResource(int formDefinitionId, FormPermissionType permissionType)
    {
        FormDefinitionId = formDefinitionId;
        PermissionType = permissionType;
    }
}
