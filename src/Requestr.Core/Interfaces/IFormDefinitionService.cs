using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IFormDefinitionService
{
    Task<List<FormDefinition>> GetFormDefinitionsAsync();
    Task<List<FormDefinition>> GetFormDefinitionsForUserAsync(string userId, List<string> userRoles);
    Task<FormDefinition?> GetFormDefinitionAsync(int id);
    Task<FormDefinition> CreateFormDefinitionAsync(FormDefinition formDefinition);
    Task<FormDefinition> UpdateFormDefinitionAsync(FormDefinition formDefinition);
    Task<bool> DeleteFormDefinitionAsync(int id);
    Task<List<FormDefinition>> GetAllAsync();
    Task<List<FormDefinition>> GetActiveAsync();
    Task<FormDefinition?> GetByIdAsync(int id);
    Task<FormDefinition> CreateAsync(FormDefinition formDefinition);
    Task<FormDefinition> UpdateAsync(FormDefinition formDefinition);
    Task<bool> DeleteAsync(int id);
}
