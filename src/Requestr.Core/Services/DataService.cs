using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using System.Text;

namespace Requestr.Core.Services;

public class DataService : IDataService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DataService> _logger;
    private readonly Dictionary<string, string> _connectionStrings;

    public DataService(IConfiguration configuration, ILogger<DataService> logger)
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

    public async Task<bool> InsertDataAsync(string databaseName, string tableName, string schema, Dictionary<string, object?> data)
    {
        var result = await InsertDataWithIdAsync(databaseName, tableName, schema, data);
        return result.Success;
    }

    public async Task<(bool Success, object? InsertedId)> InsertDataWithIdAsync(string databaseName, string tableName, string schema, Dictionary<string, object?> data)
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

            // Discover auto-generated columns we must not include in INSERTs
            var identityColumnSql = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @tableName 
                    AND TABLE_SCHEMA = @schema
                    AND COLUMNPROPERTY(OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity') = 1";
            var identityColumn = await connection.QueryFirstOrDefaultAsync<string>(identityColumnSql, new { tableName, schema });

            var computedAndRowVersionSql = @"
                SELECT c.COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_NAME = @tableName
                  AND c.TABLE_SCHEMA = @schema
                  AND (
                        COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsComputed') = 1
                        OR c.DATA_TYPE IN ('timestamp', 'rowversion')
                  )";
            var computedAndRowVersion = (await connection.QueryAsync<string>(computedAndRowVersionSql, new { tableName, schema })).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var excluded = new HashSet<string>(computedAndRowVersion, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(identityColumn)) excluded.Add(identityColumn);

            // Filter out any auto-generated columns from the payload
            var allowedData = data
                .Where(kvp => !excluded.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            string sql;
            object? insertedId = null;

            if (allowedData.Count == 0)
            {
                // No user-specified columns remain; insert DEFAULT VALUES
                if (!string.IsNullOrEmpty(identityColumn))
                {
                    sql = $"INSERT INTO [{schema}].[{tableName}] DEFAULT VALUES; SELECT CAST(SCOPE_IDENTITY() AS sql_variant);";
                    insertedId = await connection.ExecuteScalarAsync<object>(sql);
                }
                else
                {
                    sql = $"INSERT INTO [{schema}].[{tableName}] DEFAULT VALUES";
                    var rows = await connection.ExecuteAsync(sql);
                    if (rows > 0)
                    {
                        // Fallback: attempt to select using no criteria is not possible; mark success without ID
                        insertedId = null;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(identityColumn))
            {
                // Use OUTPUT to capture identity
                var columns = string.Join(", ", allowedData.Keys.Select(k => $"[{k}]"));
                var parameters = string.Join(", ", allowedData.Keys.Select(k => $"@{k}"));
                sql = $"INSERT INTO [{schema}].[{tableName}] ({columns}) OUTPUT INSERTED.[{identityColumn}] VALUES ({parameters})";
                insertedId = await connection.QuerySingleAsync<object>(sql, allowedData);
            }
            else
            {
                // No identity column on table
                var columns = string.Join(", ", allowedData.Keys.Select(k => $"[{k}]"));
                var parameters = string.Join(", ", allowedData.Keys.Select(k => $"@{k}"));
                sql = $"INSERT INTO [{schema}].[{tableName}] ({columns}) VALUES ({parameters})";
                var rowsAffected = await connection.ExecuteAsync(sql, allowedData);
                if (rowsAffected > 0)
                {
                    // Try to get the inserted record using provided data as a key
                    var whereClause = string.Join(" AND ", allowedData.Keys.Select(k => $"[{k}] = @{k}"));
                    var selectSql = $"SELECT TOP 1 * FROM [{schema}].[{tableName}] WHERE {whereClause}";
                    var insertedRecord = await connection.QueryFirstOrDefaultAsync<dynamic>(selectSql, allowedData);
                    if (insertedRecord != null)
                    {
                        var dict = (IDictionary<string, object>)insertedRecord;
                        insertedId = dict.Values.FirstOrDefault();
                    }
                }
            }
            
            var success = insertedId != null;
            
            _logger.LogInformation("Inserted data into {Schema}.{TableName} in database {DatabaseName}. Success: {Success}, InsertedId: {InsertedId}", 
                schema, tableName, databaseName, success, insertedId);
            
            return (success, insertedId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inserting data into {Schema}.{TableName} in database {DatabaseName}. Columns provided: {Columns}", 
                schema, tableName, databaseName, string.Join(", ", data.Keys));
            throw;
        }
    }

    public async Task<bool> UpdateDataAsync(string databaseName, string tableName, string schema, Dictionary<string, object?> data, Dictionary<string, object?> whereConditions)
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

            // Determine columns that must not be updated (identity, computed/rowversion, primary keys, and WHERE columns)
            var identityColumnSql = @"
                SELECT COLUMN_NAME 
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_NAME = @tableName 
                    AND TABLE_SCHEMA = @schema
                    AND COLUMNPROPERTY(OBJECT_ID(TABLE_SCHEMA + '.' + TABLE_NAME), COLUMN_NAME, 'IsIdentity') = 1";
            var identityColumn = await connection.QueryFirstOrDefaultAsync<string>(identityColumnSql, new { tableName, schema });

            var computedAndRowVersionSql = @"
                SELECT c.COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_NAME = @tableName
                  AND c.TABLE_SCHEMA = @schema
                  AND (
                        COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME), c.COLUMN_NAME, 'IsComputed') = 1
                        OR c.DATA_TYPE IN ('timestamp', 'rowversion')
                  )";
            var computedAndRowVersion = (await connection.QueryAsync<string>(computedAndRowVersionSql, new { tableName, schema }))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Primary key columns
            var primaryKeySql = @"
                SELECT col.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                    ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                    AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                    AND tc.TABLE_NAME = kcu.TABLE_NAME
                INNER JOIN INFORMATION_SCHEMA.COLUMNS col
                    ON kcu.COLUMN_NAME = col.COLUMN_NAME
                    AND kcu.TABLE_SCHEMA = col.TABLE_SCHEMA
                    AND kcu.TABLE_NAME = col.TABLE_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                    AND tc.TABLE_NAME = @tableName
                    AND tc.TABLE_SCHEMA = @schema
                ORDER BY kcu.ORDINAL_POSITION";
            var primaryKeyColumns = (await connection.QueryAsync<string>(primaryKeySql, new { tableName, schema })).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var excluded = new HashSet<string>(computedAndRowVersion, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(identityColumn)) excluded.Add(identityColumn);
            foreach (var pk in primaryKeyColumns) excluded.Add(pk);
            foreach (var wc in whereConditions.Keys) excluded.Add(wc);

            // Filter data to only updatable columns
            var updatableData = data
                .Where(kvp => !excluded.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            // First, check if the record exists
            var whereClause = string.Join(" AND ", whereConditions.Keys.Select(k => $"[{k}] = @where_{k}"));
            var checkSql = $"SELECT COUNT(*) FROM [{schema}].[{tableName}] WHERE {whereClause}";
            
            var whereParameters = new Dictionary<string, object?>();
            foreach (var condition in whereConditions)
            {
                whereParameters[$"where_{condition.Key}"] = condition.Value;
            }
            
            var recordCount = await connection.QuerySingleAsync<int>(checkSql, whereParameters);
            
            if (recordCount == 0)
            {
                _logger.LogWarning("No records found to update in {Schema}.{TableName} with conditions: {Conditions}", 
                    schema, tableName, string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                throw new InvalidOperationException($"No records found to update. WHERE conditions: {string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
            }

            if (updatableData.Count == 0)
            {
                _logger.LogInformation("No updatable columns for {Schema}.{TableName} after excluding identity/computed/PK/WHERE. Skipping UPDATE.", schema, tableName);
                return true; // Treat as success: nothing to update
            }

            var setClause = string.Join(", ", updatableData.Keys.Select(k => $"[{k}] = @{k}"));
            var sql = $"UPDATE [{schema}].[{tableName}] SET {setClause} WHERE {whereClause}";

            // Combine parameters
            var parameters = new Dictionary<string, object?>(updatableData);
            foreach (var condition in whereConditions)
            {
                parameters[$"where_{condition.Key}"] = condition.Value;
            }

            _logger.LogInformation("Executing UPDATE SQL: {Sql} with parameters: {Parameters}", 
                sql, string.Join(", ", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}")));

            var rowsAffected = await connection.ExecuteAsync(sql, parameters);

            _logger.LogInformation("Updated data in {Schema}.{TableName} in database {DatabaseName}. Rows affected: {RowsAffected}", 
                schema, tableName, databaseName, rowsAffected);

            if (rowsAffected == 0)
            {
                throw new InvalidOperationException($"UPDATE operation affected 0 rows. This may indicate a data type mismatch or constraint violation.");
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating data in {Schema}.{TableName} in database {DatabaseName}. Data: {Data}, WhereConditions: {WhereConditions}", 
                schema, tableName, databaseName, 
                string.Join(", ", data.Select(kvp => $"{kvp.Key}={kvp.Value}")),
                string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}")));
            throw;
        }
    }

    public async Task<bool> DeleteDataAsync(string databaseName, string tableName, string schema, Dictionary<string, object?> whereConditions)
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

            var whereClause = string.Join(" AND ", whereConditions.Keys.Select(k => $"[{k}] = @{k}"));
            
            var sql = $"DELETE FROM [{schema}].[{tableName}] WHERE {whereClause}";
            
            var rowsAffected = await connection.ExecuteAsync(sql, whereConditions);
            
            _logger.LogInformation("Deleted data from {Schema}.{TableName} in database {DatabaseName}. Rows affected: {RowsAffected}", 
                schema, tableName, databaseName, rowsAffected);
            
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting data from {Schema}.{TableName} in database {DatabaseName}", 
                schema, tableName, databaseName);
            throw;
        }
    }

    public async Task<List<Dictionary<string, object?>>> QueryDataAsync(string databaseName, string tableName, string schema, Dictionary<string, object?>? whereConditions = null)
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

            var sql = $"SELECT * FROM [{schema}].[{tableName}]";
            object? parameters = null;

            if (whereConditions != null && whereConditions.Any())
            {
                var whereClause = string.Join(" AND ", whereConditions.Keys.Select(k => $"[{k}] = @{k}"));
                sql += $" WHERE {whereClause}";
                parameters = whereConditions;
            }

            var result = await connection.QueryAsync(sql, parameters);

            return result.Cast<IDictionary<string, object>>()
                        .Select(row => row.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value))
                        .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying data from {Schema}.{TableName} in database {DatabaseName}", 
                schema, tableName, databaseName);
            throw;
        }
    }

    // Additional methods for UI support
    public async Task<Dictionary<string, string>> GetRecordSummariesAsync(string connectionStringName, string tableName, string schema = "dbo")
    {
        var connectionString = GetConnectionString(connectionStringName);
        var summaries = new Dictionary<string, string>();
        
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        // Get the first few columns to create a summary
        var columnsSql = @"
            SELECT TOP 3 COLUMN_NAME 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @tableName 
                AND TABLE_SCHEMA = @schema
            ORDER BY ORDINAL_POSITION";
        
        var columns = await connection.QueryAsync<string>(columnsSql, new { tableName, schema });
        var columnList = columns.ToList();
        
        if (!columnList.Any()) return summaries;
        
        // Build a dynamic query to get records with their display values
        var selectColumns = string.Join(", ", columnList.Take(2)); // Take first 2 columns for summary
        var primaryKeyColumn = columnList.First(); // Assume first column is a key for now
        
        var dataSql = $@"
            SELECT TOP 50 
                {primaryKeyColumn} as Id,
                CONCAT({string.Join(", ' - ', ", columnList.Take(2))}) as DisplayText
            FROM [{schema}].[{tableName}]
            ORDER BY {primaryKeyColumn}";
        
        try
        {
            var records = await connection.QueryAsync<dynamic>(dataSql);
            foreach (var record in records)
            {
                var dict = (IDictionary<string, object>)record;
                var id = dict["Id"]?.ToString() ?? "";
                var displayText = dict["DisplayText"]?.ToString() ?? "";
                summaries[id] = displayText;
            }
        }
        catch
        {
            // If the dynamic query fails, return empty summaries
            return summaries;
        }
        
        return summaries;
    }

    public async Task<Dictionary<string, object?>> GetRecordByIdAsync(string connectionStringName, string tableName, string recordId, string schema = "dbo")
    {
        var connectionString = GetConnectionString(connectionStringName);
        var record = new Dictionary<string, object?>();
        
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        // Get all columns for the table
        var columnsSql = @"
            SELECT COLUMN_NAME 
            FROM INFORMATION_SCHEMA.COLUMNS 
            WHERE TABLE_NAME = @tableName 
                AND TABLE_SCHEMA = @schema
            ORDER BY ORDINAL_POSITION";
        
        var columns = await connection.QueryAsync<string>(columnsSql, new { tableName, schema });
        var columnList = columns.ToList();
        
        if (!columnList.Any()) return record;
        
        // Assume the first column is the primary key
        var primaryKeyColumn = columnList.First();
        var selectColumns = string.Join(", ", columnList);
        
        var dataSql = $@"
            SELECT {selectColumns}
            FROM [{schema}].[{tableName}]
            WHERE [{primaryKeyColumn}] = @recordId";
        
        try
        {
            var result = await connection.QueryFirstOrDefaultAsync<dynamic>(dataSql, new { recordId });
            if (result != null)
            {
                var dict = (IDictionary<string, object>)result;
                foreach (var kvp in dict)
                {
                    record[kvp.Key] = kvp.Value;
                }
            }
        }
        catch
        {
            // If the query fails, return empty record
            return record;
        }
        
        return record;
    }

    public async Task<List<string>> GetPrimaryKeyColumnsAsync(string databaseName, string tableName, string schema = "dbo")
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
                SELECT col.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                    ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                    AND tc.TABLE_SCHEMA = kcu.TABLE_SCHEMA
                    AND tc.TABLE_NAME = kcu.TABLE_NAME
                INNER JOIN INFORMATION_SCHEMA.COLUMNS col
                    ON kcu.COLUMN_NAME = col.COLUMN_NAME
                    AND kcu.TABLE_SCHEMA = col.TABLE_SCHEMA
                    AND kcu.TABLE_NAME = col.TABLE_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
                    AND tc.TABLE_NAME = @tableName
                    AND tc.TABLE_SCHEMA = @schema
                ORDER BY kcu.ORDINAL_POSITION";

            var primaryKeyColumns = await connection.QueryAsync<string>(sql, new { tableName, schema });
            return primaryKeyColumns.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting primary key columns for {Schema}.{TableName} in database {DatabaseName}", 
                schema, tableName, databaseName);
            throw;
        }
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
