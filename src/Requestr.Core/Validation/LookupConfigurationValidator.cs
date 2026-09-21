using System.ComponentModel.DataAnnotations;
using Requestr.Core.Models;

namespace Requestr.Core.Validation;

public static class LookupConfigurationValidator
{
    public static void Validate(FormField field)
    {
        if (!Enum.IsDefined(field.OptionSource))
            throw new ValidationException("Unknown option source.");
        if (field.LookupSelectionLabel?.Length > 128)
            throw new ValidationException("The final selection label must not exceed 128 characters.");
        if (field.OptionSource == FieldOptionSource.Static)
            return;
        if (!string.Equals(field.ControlType, "searchable-select", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Database lookups require a searchable dropdown.");
        if (field.LookupDatabaseConnectionName?.Length > 255)
            throw new ValidationException("The lookup connection name must not exceed 255 characters.");
        if (new[] { field.LookupSchema, field.LookupTable, field.LookupKeyColumn, field.LookupLabelColumn }
            .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128))
            throw new ValidationException("Select a lookup schema, table, key column, and label column.");
        if (field.LookupFilterLevels.Any(level => string.IsNullOrWhiteSpace(level.Column) || level.Column.Length > 128 ||
            string.IsNullOrWhiteSpace(level.Label) || level.Label.Length > 128))
            throw new ValidationException("Each lookup filter level requires a source column and a label of up to 128 characters.");
        if (field.LookupFilterLevels.Select(level => level.Column).Distinct(StringComparer.OrdinalIgnoreCase).Count() != field.LookupFilterLevels.Count)
            throw new ValidationException("A lookup source column can only be used once in the filter levels.");
    }
}