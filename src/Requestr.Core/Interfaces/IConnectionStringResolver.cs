namespace Requestr.Core.Interfaces;

/// <summary>
/// Provides connection string resolution from various configuration sources.
/// Checks both ConnectionStrings and DatabaseConnections sections of configuration.
/// </summary>
public interface IConnectionStringResolver
{
    /// <summary>
    /// Gets a connection string by name, checking both ConnectionStrings and DatabaseConnections sections.
    /// </summary>
    /// <param name="connectionStringName">The name of the connection string to resolve.</param>
    /// <returns>The resolved connection string, or null if not found.</returns>
    string? GetConnectionString(string connectionStringName);
    
    /// <summary>
    /// Gets a connection string by name, throwing an exception if not found.
    /// </summary>
    /// <param name="connectionStringName">The name of the connection string to resolve.</param>
    /// <returns>The resolved connection string.</returns>
    /// <exception cref="ArgumentException">Thrown when the connection string is not found.</exception>
    string GetConnectionStringOrThrow(string connectionStringName);
}
