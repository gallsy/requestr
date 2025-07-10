namespace Requestr.Core.Models;

public class FormDefinition : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DatabaseConnectionName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public List<FormField> Fields { get; set; } = new();
    public List<string> ApproverRoles { get; set; } = new();
    public bool RequiresApproval { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public class FormField : BaseEntity
{
    public int FormDefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ControlType { get; set; } = string.Empty; // input, textarea, checkbox, select, date, datetime, time
    public int MaxLength { get; set; }
    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsVisibleInDataView { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string? ValidationRegex { get; set; }
    public string? ValidationMessage { get; set; }
    public string? VisibilityCondition { get; set; }
    public string? DropdownOptions { get; set; } // JSON array of options for select controls
    public int DisplayOrder { get; set; }
}
