using Microsoft.Data.SqlClient;
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
    /// Applies form request changes to the target database within an existing transaction.
    /// </summary>
    Task<ApplicationResult> ApplyChangesToDatabaseAsync(FormRequest formRequest, SqlConnection? connection, SqlTransaction? transaction);
    
    /// <summary>
    /// Manually applies an approved request that wasn't automatically applied.
    /// </summary>
    Task<bool> ManuallyApplyAsync(int formRequestId);
}
