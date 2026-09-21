using System.ComponentModel.DataAnnotations;

namespace Requestr.Core.Models.DTOs;

public class UpdateFormDesignDto
{
    public int FormDefinitionId { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
    public List<FormDesignFieldDto> Fields { get; set; } = new();
    public List<FormDesignSectionDto> Sections { get; set; } = new();

    public static UpdateFormDesignDto FromForm(FormDefinition form) => new()
    {
        FormDefinitionId = form.Id,
        Version = form.DesignVersion?.ToArray() ?? Array.Empty<byte>(),
        Fields = form.Fields.Select(field => new FormDesignFieldDto
        {
            Id = field.Id,
            DisplayName = field.DisplayName,
            HelpText = field.HelpText,
            DropdownOptions = field.DropdownOptions,
            DisplayOrder = field.DisplayOrder,
            FormSectionId = field.FormSectionId,
            GridRow = field.GridRow,
            GridColumn = field.GridColumn,
            GridColumnSpan = field.GridColumnSpan
        }).ToList(),
        Sections = form.Sections.Select(section => new FormDesignSectionDto
        {
            Id = section.Id,
            Name = section.Name,
            Description = section.Description,
            DisplayOrder = section.DisplayOrder,
            MaxColumns = section.MaxColumns
        }).ToList()
    };

    public void ValidateAgainst(FormDefinition current)
    {
        if (FormDefinitionId != current.Id || Version.Length != 8 ||
            current.DesignVersion == null || !Version.SequenceEqual(current.DesignVersion))
            throw new InvalidOperationException("This form has changed. Reload it before saving your edits.");

        if (Fields.Select(field => field.Id).Distinct().Count() != Fields.Count ||
            !Fields.Select(field => field.Id).ToHashSet().SetEquals(current.Fields.Select(field => field.Id)))
            throw new ValidationException("Adding or removing fields requires Admin access.");

        if (Sections.Any(section => section.Id == 0) ||
            Sections.Select(section => section.Id).Distinct().Count() != Sections.Count)
            throw new ValidationException("Section identifiers must be unique and nonzero.");

        foreach (var section in Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Name) || section.Name.Length > 255 ||
                section.MaxColumns is < 1 or > 12 || section.DisplayOrder < 0)
                throw new ValidationException("Each section needs a title, a valid order, and between 1 and 12 columns.");
        }

        var currentSections = current.Sections.ToDictionary(section => section.Id);
        foreach (var section in current.Sections.Where(section => !string.IsNullOrWhiteSpace(section.VisibilityCondition)))
        {
            if (!Sections.Any(candidate => candidate.Id == section.Id))
                throw new ValidationException("Removing a section with a visibility condition requires Admin access.");
        }

        var currentFields = current.Fields.ToDictionary(field => field.Id);
        foreach (var field in Fields)
        {
            var original = currentFields[field.Id];
            var section = Sections.FirstOrDefault(candidate => candidate.Id == field.FormSectionId);
            if (field.FormSectionId.HasValue && section == null)
                throw new ValidationException("A field refers to a section that is not in this form.");

            var maxColumns = section?.MaxColumns ?? 12;
            if (string.IsNullOrWhiteSpace(field.DisplayName) || field.DisplayName.Length > 255 ||
                field.DisplayOrder < 0 || field.GridRow is < 1 or > 1000 ||
                field.GridColumn < 1 || field.GridColumnSpan < 1 ||
                field.GridColumn > maxColumns || field.GridColumnSpan > maxColumns - field.GridColumn + 1)
                throw new ValidationException($"Check the label and grid position for '{original.Name}'.");

            var originalCondition = original.FormSectionId.HasValue && currentSections.TryGetValue(original.FormSectionId.Value, out var originalSection)
                ? originalSection.VisibilityCondition : null;
            var targetCondition = field.FormSectionId.HasValue && currentSections.TryGetValue(field.FormSectionId.Value, out var targetSection)
                ? targetSection.VisibilityCondition : null;
            if (!string.Equals(originalCondition ?? "", targetCondition ?? "", StringComparison.Ordinal))
                throw new ValidationException("Moving fields between sections with different visibility conditions requires Admin access.");

            var controlType = string.IsNullOrEmpty(original.ControlType) ? original.DataType : original.ControlType;
            if (field.DropdownOptions != original.DropdownOptions &&
                (current.Fields.Any(candidate => candidate.LookupParentField == original.Name ||
                    Requestr.Core.Validation.FormConditions.Parse(candidate.VisibilityCondition)?.Field == original.Name) ||
                 current.Sections.Any(candidate => Requestr.Core.Validation.FormConditions.Parse(candidate.VisibilityCondition)?.Field == original.Name)))
                throw new ValidationException("Changing options referenced by conditions requires Admin access.");
            if (field.DropdownOptions != original.DropdownOptions &&
                (original.OptionSource != FieldOptionSource.Static || controlType is not ("select" or "searchable-select" or "radio")))
                throw new ValidationException("Only static choice fields can have their options edited.");
        }
    }
}

public class FormDesignFieldDto
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? HelpText { get; set; }
    public string? DropdownOptions { get; set; }
    public int DisplayOrder { get; set; }
    public int? FormSectionId { get; set; }
    public int GridRow { get; set; }
    public int GridColumn { get; set; }
    public int GridColumnSpan { get; set; }
}

public class FormDesignSectionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public int MaxColumns { get; set; }
}