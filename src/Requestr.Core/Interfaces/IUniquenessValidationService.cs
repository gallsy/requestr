using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IUniquenessValidationService
{
    /// <summary>
    /// Validates uniqueness constraints for a form submission against the target table and pending requests.
    /// Returns a list of violation messages (empty if no violations).
    /// </summary>
    /// <param name="excludeRequestId">Optional request ID to exclude from pending request checks (to avoid self-detection).</param>
    Task<List<string>> ValidateAsync(int formDefinitionId, Dictionary<string, object?> fieldValues, RequestType requestType, Dictionary<string, object?>? originalValues = null, int? excludeRequestId = null);
}
