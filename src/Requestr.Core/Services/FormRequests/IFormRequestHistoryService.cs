using Requestr.Core.Models;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Service for managing form request history and audit trails.
/// </summary>
public interface IFormRequestHistoryService
{
    /// <summary>
    /// Gets the history entries for a form request.
    /// </summary>
    Task<List<FormRequestHistory>> GetHistoryAsync(int formRequestId);
    
    /// <summary>
    /// Adds a history entry for a form request.
    /// </summary>
    Task<FormRequestHistory> AddHistoryAsync(FormRequestHistory history);
    
    /// <summary>
    /// Records a change to a form request.
    /// </summary>
    Task RecordChangeAsync(int formRequestId, FormRequestChangeType changeType,
        Dictionary<string, object?>? previousValues, Dictionary<string, object?>? newValues,
        string changedBy, string changedByName, string? comments = null);
    
    /// <summary>
    /// Records a change to a form request within an existing connection and transaction.
    /// </summary>
    Task RecordChangeAsync(int formRequestId, FormRequestChangeType changeType,
        Dictionary<string, object?>? previousValues, Dictionary<string, object?>? newValues,
        string changedBy, string changedByName, string? comments,
        System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
    
    /// <summary>
    /// Gets debug information about a form request's history.
    /// </summary>
    Task<string> GetDebugInfoAsync(int formRequestId);
}
