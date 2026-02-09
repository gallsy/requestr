using Requestr.Core.Models;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Service for applying approved form requests to target databases.
/// Handles INSERT, UPDATE, and DELETE operations.
/// </summary>
public interface IFormRequestApplicationService
{
    /// <summary>
    /// Applies an approved form request to the target database.
    /// </summary>
    Task<bool> ApplyAsync(int formRequestId);
    
    /// <summary>
    /// Applies form request changes to the target database.
    /// </summary>
    Task<ApplicationResult> ApplyChangesToDatabaseAsync(FormRequest formRequest);
    
    /// <summary>
    /// Manually applies an approved request that wasn't automatically applied.
    /// </summary>
    Task<bool> ManuallyApplyAsync(int formRequestId);

    /// <summary>
    /// Gets diagnostic information about a form request's workflow status.
    /// </summary>
    Task<string> GetWorkflowDiagnosticsAsync(int formRequestId);
}
