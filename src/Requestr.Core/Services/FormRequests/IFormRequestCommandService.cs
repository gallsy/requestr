using Requestr.Core.Models;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Service for creating and updating form requests.
/// </summary>
public interface IFormRequestCommandService
{
    /// <summary>
    /// Creates a new form request. Starts workflow if configured.
    /// </summary>
    Task<FormRequest> CreateAsync(FormRequest formRequest);
    
    /// <summary>
    /// Updates an existing form request.
    /// </summary>
    Task<FormRequest> UpdateAsync(FormRequest formRequest);
    
    /// <summary>
    /// Deletes a form request.
    /// </summary>
    Task<bool> DeleteAsync(int id);
    
    /// <summary>
    /// Updates the status of a form request.
    /// </summary>
    Task UpdateStatusAsync(int id, RequestStatus status, string? failureMessage = null);
    
    /// <summary>
    /// Updates field values for a form request.
    /// </summary>
    Task UpdateFieldValuesAsync(int id, Dictionary<string, object?> fieldValues);
}
