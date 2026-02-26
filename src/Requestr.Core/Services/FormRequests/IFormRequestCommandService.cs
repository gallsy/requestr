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
    /// Cancels a pending form request. Also cancels any associated workflow.
    /// </summary>
    /// <param name="id">The form request ID.</param>
    /// <param name="userId">The user ID of the requester.</param>
    /// <param name="userName">The display name of the requester.</param>
    /// <returns>True if the request was cancelled.</returns>
    Task<bool> CancelAsync(int id, string userId, string userName);

    /// <summary>
    /// Updates field values for a form request.
    /// </summary>
    Task UpdateFieldValuesAsync(int id, Dictionary<string, object?> fieldValues);
}
