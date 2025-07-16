using System.ComponentModel.DataAnnotations;

namespace Requestr.Core.Models.DTOs;

public class CreateFormDefinitionDto
{
    [Required(ErrorMessage = "Name is required")]
    [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Category is required")]
    [MaxLength(50, ErrorMessage = "Category cannot exceed 50 characters")]
    public string Category { get; set; } = string.Empty;

    [Required(ErrorMessage = "Database connection is required")]
    public string DatabaseConnectionName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Table name is required")]
    public string TableName { get; set; } = string.Empty;

    public string Schema { get; set; } = "dbo";
    public List<CreateFormFieldDto> Fields { get; set; } = new();
    public List<CreateFormSectionDto> Sections { get; set; } = new();
    public List<string> ApproverRoles { get; set; } = new();
    public bool RequiresApproval { get; set; } = true;
}

public class UpdateFormDefinitionDto : CreateFormDefinitionDto
{
    [Required]
    public int Id { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CreateFormSectionDto
{
    [Required(ErrorMessage = "Section name is required")]
    [MaxLength(255, ErrorMessage = "Section name cannot exceed 255 characters")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000, ErrorMessage = "Description cannot exceed 1000 characters")]
    public string? Description { get; set; }

    [Range(0, 1000, ErrorMessage = "Display order must be between 0 and 1000")]
    public int DisplayOrder { get; set; } = 0;

    public bool IsCollapsible { get; set; } = false;
    public bool DefaultExpanded { get; set; } = true;
    public string? VisibilityCondition { get; set; }

    [Range(1, 12, ErrorMessage = "Max columns must be between 1 and 12")]
    public int MaxColumns { get; set; } = 12;
}

public class CreateFormFieldDto
{
    [Required(ErrorMessage = "Field name is required")]
    [MaxLength(50, ErrorMessage = "Field name cannot exceed 50 characters")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Display name is required")]
    [MaxLength(100, ErrorMessage = "Display name cannot exceed 100 characters")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Data type is required")]
    public string DataType { get; set; } = string.Empty;

    [Range(0, 8000, ErrorMessage = "Max length must be between 0 and 8000")]
    public int MaxLength { get; set; }

    public bool IsRequired { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsVisible { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string? ValidationRegex { get; set; }

    [MaxLength(200, ErrorMessage = "Validation message cannot exceed 200 characters")]
    public string? ValidationMessage { get; set; }

    public string? VisibilityCondition { get; set; }

    [Range(0, 1000, ErrorMessage = "Display order must be between 0 and 1000")]
    public int DisplayOrder { get; set; }

    // Grid positioning properties
    public int? FormSectionId { get; set; }

    [Range(1, 100, ErrorMessage = "Grid row must be between 1 and 100")]
    public int GridRow { get; set; } = 1;

    [Range(1, 12, ErrorMessage = "Grid column must be between 1 and 12")]
    public int GridColumn { get; set; } = 1;

    [Range(1, 12, ErrorMessage = "Grid column span must be between 1 and 12")]
    public int GridColumnSpan { get; set; } = 6;
}

public class FormDefinitionSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DatabaseConnectionName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public int SectionCount { get; set; }
    public int PendingRequestCount { get; set; }
}
