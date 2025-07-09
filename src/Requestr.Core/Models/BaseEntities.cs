namespace Requestr.Core.Models;

public abstract class BaseEntity
{
    public int Id { get; set; }
}

public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    
    public void SetUpdated(string updatedBy)
    {
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = updatedBy;
    }
}

public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    string CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string? UpdatedBy { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}
