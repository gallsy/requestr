using System.Security.Claims;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;
using Requestr.Core.Repositories;
using Requestr.Core.Utilities;

namespace Requestr.Core.Services;

public class FormDesignService(IFormDesignRepository repository, IFormPermissionService permissions) : IFormDesignService
{
    public async Task<bool> CanEditAsync(ClaimsPrincipal user, int formDefinitionId)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        var roles = ClaimsHelper.GetUserRoles(user);
        if (roles.Contains("Admin"))
            return true;

        foreach (var role in roles)
        {
            if (await permissions.HasPermissionAsync(formDefinitionId, role, FormPermissionType.EditFormDesign))
                return true;
        }

        return false;
    }

    public Task<List<FormDefinition>> GetEditableFormsAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return Task.FromResult(new List<FormDefinition>());

        var roles = ClaimsHelper.GetUserRoles(user);
        return repository.GetEditableFormsAsync(roles, roles.Contains("Admin"));
    }

    public async Task<FormDefinition?> GetDesignAsync(ClaimsPrincipal user, int formDefinitionId)
    {
        if (!await CanEditAsync(user, formDefinitionId))
            throw new UnauthorizedAccessException("You do not have permission to edit this form.");

        return await repository.GetAsync(formDefinitionId);
    }

    public async Task SaveAsync(ClaimsPrincipal user, UpdateFormDesignDto update)
    {
        if (!await CanEditAsync(user, update.FormDefinitionId))
            throw new UnauthorizedAccessException("You no longer have permission to edit this form.");

        var roles = ClaimsHelper.GetUserRoles(user);
        var changedBy = user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst("oid")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.Identity!.Name;
        if (string.IsNullOrWhiteSpace(changedBy))
            throw new UnauthorizedAccessException("An authenticated user identifier is required to save a form.");

        await repository.SaveAsync(update, roles, roles.Contains("Admin"), changedBy);
    }
}