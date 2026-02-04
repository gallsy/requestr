using System.Data;
using Microsoft.Data.SqlClient;

namespace Requestr.Core.Repositories;

/// <summary>
/// Factory for creating database connections with consistent configuration.
/// Centralizes connection string management and timeout settings.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates a new database connection to the Requestr application database.
    /// </summary>
    IDbConnection CreateConnection();
    
    /// <summary>
    /// Creates a new opened database connection to the Requestr application database asynchronously.
    /// </summary>
    Task<SqlConnection> CreateConnectionAsync();
    
    /// <summary>
    /// Creates a new database connection to a named external database.
    /// Used for connecting to target databases where form data is applied.
    /// </summary>
    /// <param name="connectionName">The name of the connection from configuration.</param>
    IDbConnection CreateConnection(string connectionName);
    
    /// <summary>
    /// Creates a new opened database connection to a named external database asynchronously.
    /// </summary>
    Task<SqlConnection> CreateConnectionAsync(string connectionName);
    
    /// <summary>
    /// Gets the default command timeout in seconds.
    /// </summary>
    int DefaultCommandTimeout { get; }
    
    /// <summary>
    /// Gets the timeout for long-running operations in seconds.
    /// </summary>
    int LongRunningCommandTimeout { get; }
    
    /// <summary>
    /// Gets the timeout for bulk operations in seconds.
    /// </summary>
    int BulkOperationCommandTimeout { get; }
}
