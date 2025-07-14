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
    public List<string> ApproverRoles { get; set; } = new(); // Legacy - will be replaced by workflow system
    public bool RequiresApproval { get; set; } = true;
    public bool IsActive { get; set; } = true;
    
    // Workflow system integration
    public int? WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    
    // Form-specific permissions
    public List<FormPermission> FormPermissions { get; set; } = new();
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

/// <summary>
/// Represents granular permissions for a specific form
/// </summary>
public class FormPermission : AuditableEntity
{
    public int FormDefinitionId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public FormPermissionType PermissionType { get; set; }
    public bool IsGranted { get; set; } = true;
    public string? Conditions { get; set; } // JSON for advanced conditions (future use)
    
    // Navigation property
    public FormDefinition? FormDefinition { get; set; }
}

/// <summary>
/// Types of permissions that can be granted for a form
/// </summary>
public enum FormPermissionType
{
    // Request creation permissions
    CreateRequest = 1,      // Can create new requests using the form
    UpdateRequest = 2,      // Can update existing records via requests
    DeleteRequest = 3,      // Can delete records via requests
    
    // Data view permissions
    ViewData = 10,          // Can see the data view page
    
    // Bulk operation permissions
    BulkActions = 20,       // Can perform bulk actions from data view
    BulkUploadCsv = 21,     // Can upload CSV files for bulk operations
}
