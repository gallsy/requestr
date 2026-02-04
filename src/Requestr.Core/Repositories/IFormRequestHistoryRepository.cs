using Requestr.Core.Models;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository for FormRequestHistory data access operations.
/// Handles audit trail and change tracking for form requests.
/// </summary>
public interface IFormRequestHistoryRepository
{
    /// <summary>
    /// Gets all history entries for a form request ordered by date descending.
    /// </summary>
    Task<List<FormRequestHistory>> GetByFormRequestIdAsync(int formRequestId);
    
    /// <summary>
    /// Adds a new history entry for a form request.
    /// </summary>
    Task<int> AddAsync(FormRequestHistory history);
    
    /// <summary>
    /// Adds a new history entry within an existing transaction.
    /// </summary>
    Task<int> AddAsync(FormRequestHistory history, System.Data.IDbConnection connection, System.Data.IDbTransaction transaction);
}
