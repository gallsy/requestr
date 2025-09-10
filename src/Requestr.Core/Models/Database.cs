using System.ComponentModel.DataAnnotations;

namespace Requestr.Core.Models;

public class DatabaseConnection
{
    public string Name { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class User
{
    public int Id { get; set; }
    public Guid UserObjectId { get; set; }
    public Guid? TenantId { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? UPN { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

public class TableSchema
{
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public List<ColumnInfo> Columns { get; set; } = new();
}

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsComputed { get; set; }
    public bool IsRowVersion { get; set; }
    public string? DefaultValue { get; set; }
}
