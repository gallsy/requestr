using Requestr.Core.Models;

namespace Requestr.Web.Utilities;

public static class DesignerGrid
{
    public static bool CanPlace(FormDefinition form, FormField? field, FormSection section, int row, int column, int span, bool presentationOnly)
    {
        if (!form.Sections.Contains(section) || row is < 1 or > 1000 || column < 1 || span < 1 || column > section.MaxColumns || span > section.MaxColumns - column + 1)
            return false;
        if (presentationOnly)
        {
            if (field == null || !form.Fields.Contains(field)) return false;
            var source = form.Sections.FirstOrDefault(candidate => candidate.Id == field.FormSectionId);
            if ((source?.VisibilityCondition ?? "") != (section.VisibilityCondition ?? "")) return false;
        }
        return !form.Fields.Any(other => other != field && other.FormSectionId == section.Id && other.GridRow == row &&
            other.GridColumn < column + span && other.GridColumn + other.GridColumnSpan > column);
    }

    public static bool TryPlace(FormDefinition form, FormField field, FormSection section, int row, int column, int span, bool presentationOnly)
    {
        if (!form.Fields.Contains(field) || !CanPlace(form, field, section, row, column, span, presentationOnly)) return false;
        field.FormSectionId = section.Id;
        field.GridRow = row;
        field.GridColumn = column;
        field.GridColumnSpan = span;
        return true;
    }

    public static bool CanSetColumns(FormDefinition form, FormSection section, int columns) =>
        columns is >= 1 and <= 12 && form.Fields.Where(field => field.FormSectionId == section.Id)
            .All(field => field.GridColumn + field.GridColumnSpan - 1 <= columns);

    public static int NextRow(FormDefinition form, FormSection section) =>
        form.Fields.Where(field => field.FormSectionId == section.Id).Select(field => field.GridRow).DefaultIfEmpty(0).Max() + 1;

    public static bool Arrange(FormDefinition form, FormSection section)
    {
        var positions = new List<(FormField Field, int Row, int Column, int Span)>();
        var row = 1;
        var column = 1;
        foreach (var field in form.Fields.Where(field => field.FormSectionId == section.Id).OrderBy(field => field.GridRow).ThenBy(field => field.GridColumn))
        {
            var span = Math.Clamp(field.GridColumnSpan, 1, section.MaxColumns);
            if (column + span - 1 > section.MaxColumns) { row++; column = 1; }
            if (row > 1000) return false;
            positions.Add((field, row, column, span));
            column += span;
        }
        foreach (var position in positions)
        {
            position.Field.GridRow = position.Row;
            position.Field.GridColumn = position.Column;
            position.Field.GridColumnSpan = position.Span;
        }
        return true;
    }
}