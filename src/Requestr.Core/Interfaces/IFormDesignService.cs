using System.Security.Claims;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;

namespace Requestr.Core.Interfaces;

public interface IFormDesignService
{
    Task<bool> CanEditAsync(ClaimsPrincipal user, int formDefinitionId);
    Task<List<FormDefinition>> GetEditableFormsAsync(ClaimsPrincipal user);
    Task<FormDefinition?> GetDesignAsync(ClaimsPrincipal user, int formDefinitionId);
    Task SaveAsync(ClaimsPrincipal user, UpdateFormDesignDto update);
}