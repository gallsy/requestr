using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Requestr.Core.Models;
using Requestr.Core.Utilities;

namespace Requestr.Core.Validation;

public static class FormConditions
{
    public static FormCondition? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var condition = JsonSerializer.Deserialize<FormCondition>(json);
            if (condition == null || string.IsNullOrWhiteSpace(condition.Field) || !Enum.IsDefined(condition.Operator))
                throw new ValidationException("Select a valid visibility field and operator.");
            return condition;
        }
        catch (JsonException) { throw new ValidationException("The visibility rule is invalid. Reconfigure it in the designer."); }
    }

    public static string? Serialize(FormCondition? condition) => condition == null ? null : JsonSerializer.Serialize(condition);

    public static object? Value(IReadOnlyDictionary<string, object?> values, string name)
    {
        var matches = values.Where(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count > 1) throw new ValidationException($"Duplicate values for {name}.");
        return matches.Count == 0 ? null : SqlTypeConverter.UnwrapJsonElement(matches[0].Value);
    }

    public static string Text(object? value) => Convert.ToString(SqlTypeConverter.UnwrapJsonElement(value), CultureInfo.InvariantCulture) ?? "";

    public static bool Matches(FormCondition? condition, FormDefinition form, IReadOnlyDictionary<string, object?> values)
    {
        if (condition == null) return true;
        var parent = form.Fields.SingleOrDefault(field => string.Equals(field.Name, condition.Field, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Unknown condition field: {condition.Field}.");
        var actual = Text(Value(values, parent.Name));
        var empty = string.IsNullOrWhiteSpace(actual);
        return condition.Operator switch
        {
            FormConditionOperator.IsEmpty => empty,
            FormConditionOperator.IsNotEmpty => !empty,
            FormConditionOperator.Equals => !empty && Equal(actual, condition.Value, parent),
            FormConditionOperator.NotEquals => !empty && Equal(actual, actual, parent) && !Equal(actual, condition.Value, parent),
            _ => false
        };
    }

    private static bool Equal(string actual, string? expected, FormField field)
    {
        var type = (field.SqlDataType ?? field.DataType).ToLowerInvariant();
        if (type is "bit" or "bool" or "boolean")
            return Boolean(actual) is bool actualBoolean && Boolean(expected) is bool expectedBoolean && actualBoolean == expectedBoolean;
        if (type is "int" or "bigint" or "smallint" or "tinyint" or "number" or "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney")
            return decimal.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var actualNumber) &&
                decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var expectedNumber) && actualNumber == expectedNumber;
        if (type == "uniqueidentifier") return Guid.TryParse(actual, out var actualId) && Guid.TryParse(expected, out var expectedId) && actualId == expectedId;
        return string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static bool? Boolean(string? value) => value?.ToLowerInvariant() switch { "true" or "1" => true, "false" or "0" => false, _ => null };

    public static bool IsApplicable(FormDefinition form, FormField field, IReadOnlyDictionary<string, object?> values)
    {
        var section = form.Sections.FirstOrDefault(candidate => candidate.Id == field.FormSectionId);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool Evaluate(string? json)
        {
            var condition = Parse(json);
            if (condition == null) return true;
            if (!active.Add(condition.Field)) throw new ValidationException("Circular form conditions are not supported.");
            var parent = form.Fields.SingleOrDefault(candidate => string.Equals(candidate.Name, condition.Field, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Unknown condition field: {condition.Field}.");
            var parentSection = form.Sections.FirstOrDefault(candidate => candidate.Id == parent.FormSectionId);
            var result = Evaluate(parent.VisibilityCondition) && Evaluate(parentSection?.VisibilityCondition) && Matches(condition, form, values);
            active.Remove(condition.Field);
            return result;
        }
        return Evaluate(field.VisibilityCondition) && Evaluate(section?.VisibilityCondition);
    }

    public static bool HasConditions(FormDefinition form) => form.Fields.Any(field => !string.IsNullOrWhiteSpace(field.VisibilityCondition) || !string.IsNullOrWhiteSpace(field.LookupParentField)) || form.Sections.Any(section => !string.IsNullOrWhiteSpace(section.VisibilityCondition));

    public static void ValidateConfiguration(FormDefinition form)
    {
        var fields = form.Fields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
        var dependencies = fields.Keys.ToDictionary(name => name, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        void AddRule(FormField field, string? json)
        {
            var condition = Parse(json);
            if (condition == null) return;
            if (!fields.TryGetValue(condition.Field, out var parent)) throw new ValidationException($"{field.DisplayName}: the visibility parent no longer exists.");
            ValidateParent(parent);
            if (condition.Operator is FormConditionOperator.Equals or FormConditionOperator.NotEquals && string.IsNullOrWhiteSpace(condition.Value))
                throw new ValidationException("Choose a comparison value, or use an empty/not empty rule.");
            if (condition.Value != null && condition.Operator is FormConditionOperator.Equals or FormConditionOperator.NotEquals && !Equal(condition.Value, condition.Value, parent))
                throw new ValidationException("The visibility comparison value has an invalid type.");
            dependencies[field.Name].Add(parent.Name);
        }
        foreach (var field in form.Fields)
        {
            AddRule(field, field.VisibilityCondition);
            AddRule(field, form.Sections.FirstOrDefault(section => section.Id == field.FormSectionId)?.VisibilityCondition);
            if (!string.IsNullOrEmpty(field.LookupParentField))
            {
                if (field.OptionSource != FieldOptionSource.DatabaseLookup || !fields.ContainsKey(field.LookupParentField) || field.LookupParentField.Length > 255 || string.IsNullOrWhiteSpace(field.LookupFilterColumn) || field.LookupFilterColumn.Length > 128)
                    throw new ValidationException($"{field.DisplayName}: select a lookup parent and filter column.");
                ValidateParent(fields[field.LookupParentField]);
                dependencies[field.Name].Add(field.LookupParentField);
            }
            else if (!string.IsNullOrEmpty(field.LookupFilterColumn)) throw new ValidationException("A lookup filter needs a parent field.");
        }
        foreach (var section in form.Sections)
        {
            var condition = Parse(section.VisibilityCondition);
            if (condition == null) continue;
            if (!fields.TryGetValue(condition.Field, out var parent)) throw new ValidationException($"{section.Name}: the visibility parent no longer exists.");
            ValidateParent(parent);
            if (condition.Operator is FormConditionOperator.Equals or FormConditionOperator.NotEquals &&
                (string.IsNullOrWhiteSpace(condition.Value) || !Equal(condition.Value, condition.Value, parent)))
                throw new ValidationException($"{section.Name}: select a valid comparison value.");
        }
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string name)
        {
            if (visited.Contains(name)) return;
            if (!visiting.Add(name)) throw new ValidationException("Circular field or section dependencies are not supported.");
            foreach (var parent in dependencies[name]) Visit(parent);
            visiting.Remove(name);
            visited.Add(name);
        }
        foreach (var name in fields.Keys) Visit(name);
    }

    private static void ValidateParent(FormField parent)
    {
        if (parent.ComputedValueType is not (null or ComputedValueType.None))
            throw new ValidationException($"{parent.DisplayName}: generated values cannot control conditions.");
    }
}