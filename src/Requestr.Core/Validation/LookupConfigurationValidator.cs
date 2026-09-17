using System.ComponentModel.DataAnnotations;
using Requestr.Core.Models;

namespace Requestr.Core.Validation;

public static class LookupConfigurationValidator
{
    public static void Validate(FormField field)
    {
        if (!Enum.IsDefined(field.OptionSource))
            throw new ValidationException("Unknown option source.");
        if (field.OptionSource == FieldOptionSource.Static)
            return;
        if (!string.Equals(field.ControlType, "searchable-select", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Database lookups require a searchable dropdown.");
        if (field.LookupDatabaseConnectionName?.Length > 255)
            throw new ValidationException("The lookup connection name must not exceed 255 characters.");
        if (new[] { field.LookupSchema, field.LookupTable, field.LookupKeyColumn, field.LookupLabelColumn }
            .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128))
            throw new ValidationException("Select a lookup schema, table, key column, and label column.");
    }
}