using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using System.Data;

namespace Requestr.Core.Services;

public class DatabaseService : IDatabaseService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseService> _logger;
    private readonly Dictionary<string, string> _connectionStrings;

    public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _connectionStrings = new Dictionary<string, string>();
        
        // Load predefined database connections from configuration
        var dbSection = _configuration.GetSection("DatabaseConnections");
        foreach (var child in dbSection.GetChildren())
        {
            var name = child.Key;
            var connectionString = child.Value;
            if (!string.IsNullOrEmpty(connectionString))
            {
                _connectionStrings[name] = connectionString;
            }
        }
    }

    public Task<List<string>> GetDatabaseNamesAsync()
    {
        return Task.FromResult(_connectionStrings.Keys.ToList());
    }

    public async Task<List<TableSchema>> GetTableSchemasAsync(string databaseName)
    {
        if (!_connectionStrings.ContainsKey(databaseName))
        {
            throw new ArgumentException($"Database connection '{databaseName}' not found");
        }

        var connectionString = _connectionStrings[databaseName];
        var schemas = new List<TableSchema>();

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    t.TABLE_SCHEMA,
                    t.TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLES t
                WHERE t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME";

            var tables = await connection.QueryAsync<(string Schema, string TableName)>(sql);

            foreach (var table in tables)
            {
                var tableSchema = await GetTableSchemaAsync(databaseName, table.TableName, table.Schema);
                schemas.Add(tableSchema);
            }

            return schemas;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting table schemas for database {DatabaseName}", databaseName);
            throw;
        }
    }

    public async Task<TableSchema> GetTableSchemaAsync(string databaseName, string tableName, string schema = "dbo")
    {
        if (!_connectionStrings.ContainsKey(databaseName))
        {
            throw new ArgumentException($"Database connection '{databaseName}' not found");
        }

        var connectionString = _connectionStrings[databaseName];

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT 
                    c.COLUMN_NAME as Name,
                    c.DATA_TYPE as DataType,
                    COALESCE(c.CHARACTER_MAXIMUM_LENGTH, 0) as MaxLength,
                    CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END as IsNullable,
                    CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END as IsPrimaryKey,
                    CASE WHEN c.COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') = 1 THEN 1 ELSE 0 END as IsIdentity,
                    c.COLUMN_DEFAULT as DefaultValue
                FROM INFORMATION_SCHEMA.COLUMNS c
                LEFT JOIN (
                    SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                    INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                        ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                        AND tc.TABLE_NAME = ku.TABLE_NAME
                        AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                ) pk ON c.TABLE_SCHEMA = pk.TABLE_SCHEMA 
                    AND c.TABLE_NAME = pk.TABLE_NAME 
                    AND c.COLUMN_NAME = pk.COLUMN_NAME
                WHERE c.TABLE_NAME = @TableName AND c.TABLE_SCHEMA = @Schema
                ORDER BY c.ORDINAL_POSITION";

            var columns = await connection.QueryAsync<ColumnInfo>(sql, new { TableName = tableName, Schema = schema });

            return new TableSchema
            {
                TableName = tableName,
                Schema = schema,
                Columns = columns.ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting table schema for {Schema}.{TableName} in database {DatabaseName}", 
                schema, tableName, databaseName);
            throw;
        }
    }

    public async Task<List<Dictionary<string, object?>>> GetTableDataAsync(string databaseName, string tableName, string schema = "dbo", int limit = 100)
    {
        if (!_connectionStrings.ContainsKey(databaseName))
        {
            throw new ArgumentException($"Database connection '{databaseName}' not found");
        }

        var connectionString = _connectionStrings[databaseName];

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = $"SELECT TOP ({limit}) * FROM [{schema}].[{tableName}]";
            var result = await connection.QueryAsync(sql);

            return result.Cast<IDictionary<string, object>>()
                        .Select(row => row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value))
                        .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting table data for {Schema}.{TableName} in database {DatabaseName}", 
                schema, tableName, databaseName);
            throw;
        }
    }

    public async Task<bool> TestConnectionAsync(string databaseName)
    {
        if (!_connectionStrings.ContainsKey(databaseName))
        {
            return false;
        }

        var connectionString = _connectionStrings[databaseName];

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed for database {DatabaseName}", databaseName);
            return false;
        }
    }

    // Additional methods for UI support
    public async Task<Dictionary<string, string>> GetConnectionStringsAsync()
    {
        // Return configured connection strings from configuration
        var connectionStrings = new Dictionary<string, string>();
        
        await Task.CompletedTask; // Make it properly async
        
        // Add the configured database connections
        if (_configuration.GetConnectionString("DefaultConnection") != null)
        {
            connectionStrings.Add("DefaultConnection", "Default Application Database");
        }
        
        // Add additional database connections from DatabaseConnections section
        var dbConnections = _configuration.GetSection("DatabaseConnections");
        foreach (var child in dbConnections.GetChildren())
        {
            connectionStrings.Add(child.Key, child.Key);
        }
        
        return connectionStrings;
    }

    public async Task<List<string>> GetTablesAsync(string connectionStringName)
    {
        var connectionString = GetConnectionString(connectionStringName);
        var tables = new List<string>();
        
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        var sql = @"
            SELECT TABLE_NAME 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_TYPE = 'BASE TABLE' 
                AND TABLE_SCHEMA = 'dbo'
            ORDER BY TABLE_NAME";
        
        var result = await connection.QueryAsync<string>(sql);
        return result.ToList();
    }

    public async Task<List<ColumnInfo>> GetTableColumnsAsync(string connectionStringName, string tableName)
    {
        var connectionString = GetConnectionString(connectionStringName);
        var columns = new List<ColumnInfo>();
        
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        var sql = @"
            SELECT 
                c.COLUMN_NAME as Name,
                c.DATA_TYPE as DataType,
                COALESCE(c.CHARACTER_MAXIMUM_LENGTH, 0) as MaxLength,
                CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END as IsNullable,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END as IsPrimaryKey,
                CASE WHEN COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsIdentity') = 1 THEN 1 ELSE 0 END as IsIdentity,
                c.COLUMN_DEFAULT as DefaultValue
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_TYPE = 'PRIMARY KEY' 
                    AND tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    AND tc.TABLE_NAME = ku.TABLE_NAME
            ) pk ON c.TABLE_NAME = pk.TABLE_NAME AND c.COLUMN_NAME = pk.COLUMN_NAME
            WHERE c.TABLE_NAME = @tableName
                AND c.TABLE_SCHEMA = 'dbo'
            ORDER BY c.ORDINAL_POSITION";
        
        var result = await connection.QueryAsync<ColumnInfo>(sql, new { tableName });
        return result.ToList();
    }

    private string GetConnectionString(string connectionStringName)
    {
        // Try to get from ConnectionStrings section first
        var connectionString = _configuration.GetConnectionString(connectionStringName);
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }
        
        // Try to get from DatabaseConnections section
        connectionString = _configuration.GetSection($"DatabaseConnections:{connectionStringName}").Value;
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }
        
        throw new ArgumentException($"Connection string '{connectionStringName}' not found in configuration.");
    }
}
