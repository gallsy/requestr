using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;

namespace Requestr.Core.Repositories;

public interface IFormDesignRepository
{
    Task<List<FormDefinition>> GetEditableFormsAsync(List<string> roles, bool isAdmin);
    Task<FormDefinition?> GetAsync(int formDefinitionId);
    Task SaveAsync(UpdateFormDesignDto update, List<string> roles, bool isAdmin, string changedBy);
}