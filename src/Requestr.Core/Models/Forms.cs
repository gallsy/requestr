namespace Requestr.Core.Models;

public class FormDefinition : AuditableEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DatabaseConnectionName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public List<FormField> Fields { get; set; } = new();
    public List<FormSection> Sections { get; set; } = new(); // New: Form sections for layout
    public List<string> ApproverRoles { get; set; } = new(); // Legacy - will be replaced by workflow system
    public bool RequiresApproval { get; set; } = true;
    public bool IsActive { get; set; } = true;
    
    // Soft delete support
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    
    // Workflow system integration
    public int? WorkflowDefinitionId { get; set; }
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    
    // Form-specific permissions
    public List<FormPermission> FormPermissions { get; set; } = new();
    
    // Notification settings
    public string? NotificationEmail { get; set; }
    public bool NotifyOnCreation { get; set; } = false;
    public bool NotifyOnCompletion { get; set; } = false;
}

/// <summary>
/// Represents a logical section within a form for better organization and layout
/// </summary>
public class FormSection : AuditableEntity
{
    public int FormDefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; } = 0;
    public bool IsCollapsible { get; set; } = false;
    public bool DefaultExpanded { get; set; } = true;
    public string? VisibilityCondition { get; set; }
    public int MaxColumns { get; set; } = 12; // Grid system columns (1-12)
    
    // Navigation properties
    public FormDefinition? FormDefinition { get; set; }
    public List<FormField> Fields { get; set; } = new();
}

public class FormField : BaseEntity
{
    public int FormDefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ControlType { get; set; } = string.Empty; // input, textarea, checkbox, select, date, datetime, time
    public string? SqlDataType { get; set; } // Original SQL Server data type (e.g., "nvarchar", "int", "bit") for schema drift detection
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
    public bool TreatBlankAsNull { get; set; } = false;
    public string? HelpText { get; set; } // Tooltip text shown via info icon on the field label
    
    // Grid positioning properties
    public int? FormSectionId { get; set; }
    public int GridRow { get; set; } = 1;        // Row within the section (1-based)
    public int GridColumn { get; set; } = 1;     // Starting column (1-12)
    public int GridColumnSpan { get; set; } = 6; // Number of columns to span (1-12)
    
    // Navigation properties
    public FormSection? FormSection { get; set; }
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
