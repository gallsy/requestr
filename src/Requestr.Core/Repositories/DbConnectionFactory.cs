using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Requestr.Core.Repositories;

/// <summary>
/// Factory for creating database connections with consistent configuration.
/// </summary>
public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _defaultConnectionString;
    private readonly Dictionary<string, string> _namedConnectionStrings;
    private readonly DatabaseTimeoutOptions _timeoutOptions;
    
    public DbConnectionFactory(IConfiguration configuration)
    {
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
        
        _namedConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        // Include ConnectionStrings entries so forms targeting "DefaultConnection" resolve correctly
        var connectionStringsSection = configuration.GetSection("ConnectionStrings");
        foreach (var child in connectionStringsSection.GetChildren())
        {
            if (!string.IsNullOrEmpty(child.Value))
            {
                _namedConnectionStrings[child.Key] = child.Value;
            }
        }

        // Load named database connections from configuration (overrides any ConnectionStrings with same key)
        var dbSection = configuration.GetSection("DatabaseConnections");
        foreach (var child in dbSection.GetChildren())
        {
            var name = child.Key;
            var connectionString = child.Value;
            if (!string.IsNullOrEmpty(connectionString))
            {
                _namedConnectionStrings[name] = connectionString;
            }
        }
        
        // Load timeout options from configuration or use defaults
        _timeoutOptions = new DatabaseTimeoutOptions();
        var timeoutSection = configuration.GetSection("DatabaseTimeouts");
        if (timeoutSection.Exists())
        {
            if (int.TryParse(timeoutSection["DefaultQueryTimeout"], out var defaultTimeout))
                _timeoutOptions.DefaultQueryTimeout = defaultTimeout;
            if (int.TryParse(timeoutSection["LongRunningQueryTimeout"], out var longRunningTimeout))
                _timeoutOptions.LongRunningQueryTimeout = longRunningTimeout;
            if (int.TryParse(timeoutSection["BulkOperationTimeout"], out var bulkTimeout))
                _timeoutOptions.BulkOperationTimeout = bulkTimeout;
        }
    }
    
    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_defaultConnectionString);
    }
    
    public async Task<SqlConnection> CreateConnectionAsync()
    {
        var connection = new SqlConnection(_defaultConnectionString);
        await connection.OpenAsync();
        return connection;
    }
    
    public IDbConnection CreateConnection(string connectionName)
    {
        if (!_namedConnectionStrings.TryGetValue(connectionName, out var connectionString))
        {
            throw new ArgumentException($"Database connection '{connectionName}' not found in configuration");
        }
        return new SqlConnection(connectionString);
    }
    
    public async Task<SqlConnection> CreateConnectionAsync(string connectionName)
    {
        if (!_namedConnectionStrings.TryGetValue(connectionName, out var connectionString))
        {
            throw new ArgumentException($"Database connection '{connectionName}' not found in configuration");
        }
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
    
    public int DefaultCommandTimeout => _timeoutOptions.DefaultQueryTimeout;
    public int LongRunningCommandTimeout => _timeoutOptions.LongRunningQueryTimeout;
    public int BulkOperationCommandTimeout => _timeoutOptions.BulkOperationTimeout;
}

/// <summary>
/// Configuration options for database operation timeouts.
/// </summary>
public class DatabaseTimeoutOptions
{
    /// <summary>
    /// Timeout for standard queries (default: 30 seconds).
    /// </summary>
    public int DefaultQueryTimeout { get; set; } = 30;
    
    /// <summary>
    /// Timeout for long-running queries (default: 60 seconds).
    /// </summary>
    public int LongRunningQueryTimeout { get; set; } = 60;
    
    /// <summary>
    /// Timeout for bulk operations (default: 300 seconds).
    /// </summary>
    public int BulkOperationTimeout { get; set; } = 300;
}
