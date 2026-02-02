using Microsoft.Extensions.Configuration;
using Requestr.Core.Interfaces;

namespace Requestr.Core.Services;

/// <summary>
/// Provides connection string resolution from various configuration sources.
/// Checks both ConnectionStrings and DatabaseConnections sections of configuration.
/// </summary>
public class ConnectionStringResolver : IConnectionStringResolver
{
    private readonly IConfiguration _configuration;

    public ConnectionStringResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string? GetConnectionString(string connectionStringName)
    {
        // Try to get from ConnectionStrings section first
        var connectionString = _configuration.GetConnectionString(connectionStringName);
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }
        
        // Try to get from DatabaseConnections section
        connectionString = _configuration[$"DatabaseConnections:{connectionStringName}"];
        return connectionString;
    }

    /// <inheritdoc />
    public string GetConnectionStringOrThrow(string connectionStringName)
    {
        var connectionString = GetConnectionString(connectionStringName);
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException($"Connection string '{connectionStringName}' not found in configuration.");
        }
        return connectionString;
    }
}
