using Requestr.Core.Models;

namespace Requestr.Core.Interfaces;

public interface IDatabaseService
{
    Task<List<string>> GetDatabaseNamesAsync();
    Task<List<TableSchema>> GetTableSchemasAsync(string databaseName);
    Task<TableSchema> GetTableSchemaAsync(string databaseName, string tableName, string schema = "dbo");
    Task<List<Dictionary<string, object?>>> GetTableDataAsync(string databaseName, string tableName, string schema = "dbo", int limit = 100);
    Task<bool> TestConnectionAsync(string databaseName);
    Task<Dictionary<string, string>> GetConnectionStringsAsync();
    Task<List<string>> GetSchemasAsync(string connectionStringName);
    Task<List<string>> GetTablesAsync(string connectionStringName, string? schema = null);
    Task<List<ColumnInfo>> GetTableColumnsAsync(string connectionStringName, string tableName, string? schema = null);
}

public interface IDataService
{
    Task<bool> InsertDataAsync(string databaseName, string tableName, string schema, Dictionary<string, object?> data);
    Task<(bool Success, object? InsertedId)> InsertDataWithIdAsync(string databaseName, string tableName, string schema, Dictionary<string, object?> data);
    Task<bool> UpdateDataAsync(string databaseName, string tableName, string schema, Dictionary<string, object?> data, Dictionary<string, object?> whereConditions);
    Task<bool> DeleteDataAsync(string databaseName, string tableName, string schema, Dictionary<string, object?> whereConditions);
    Task<List<Dictionary<string, object?>>> QueryDataAsync(string databaseName, string tableName, string schema, Dictionary<string, object?>? whereConditions = null);
    Task<Dictionary<string, string>> GetRecordSummariesAsync(string connectionStringName, string tableName, string schema = "dbo");
    Task<Dictionary<string, object?>> GetRecordByIdAsync(string connectionStringName, string tableName, string recordId, string schema = "dbo");
    Task<List<string>> GetPrimaryKeyColumnsAsync(string databaseName, string tableName, string schema = "dbo");
}
