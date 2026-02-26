using System.Text.RegularExpressions;
using Requestr.Core.Models;

namespace Requestr.Core.Utilities;

/// <summary>
/// Renders webhook templates by substituting {{placeholder}} variables
/// with values from form data and system context.
/// </summary>
public static partial class WebhookTemplateEngine
{
    /// <summary>
    /// Renders a template string by replacing {{FieldName}} placeholders
    /// with corresponding values from the form request's field data and system variables.
    /// </summary>
    /// <param name="template">The template string containing {{placeholders}}.</param>
    /// <param name="fieldValues">Form field values (from the request).</param>
    /// <param name="systemVariables">System-level variables (RequestId, RequestedBy, etc.).</param>
    /// <returns>The rendered string with placeholders replaced.</returns>
    public static string Render(string template, Dictionary<string, object?> fieldValues, Dictionary<string, string>? systemVariables = null)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        return PlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;

            // Check system variables first
            if (systemVariables != null && systemVariables.TryGetValue(key, out var sysVal))
                return EscapeJsonValue(sysVal);

            // Then check form field values
            if (fieldValues.TryGetValue(key, out var fieldVal))
                return EscapeJsonValue(fieldVal?.ToString() ?? "");

            // Leave unresolved placeholders as-is
            return match.Value;
        });
    }

    /// <summary>
    /// Builds system-level variables from a form request and its form definition.
    /// </summary>
    public static Dictionary<string, string> BuildSystemVariables(FormRequest request, FormDefinition? formDefinition = null)
    {
        var vars = new Dictionary<string, string>
        {
            ["RequestId"] = request.Id.ToString(),
            ["RequestedBy"] = request.RequestedBy,
            ["RequestedByName"] = request.RequestedByName ?? request.RequestedBy,
            ["RequestedAt"] = request.RequestedAt.ToString("o"),
            ["RequestType"] = request.RequestType.ToString(),
            ["Status"] = request.Status.ToString()
        };

        if (formDefinition != null)
        {
            vars["FormName"] = formDefinition.Name;
            vars["FormId"] = formDefinition.Id.ToString();
        }

        if (request.BulkFormRequestId.HasValue)
        {
            vars["BulkFormRequestId"] = request.BulkFormRequestId.Value.ToString();
        }

        return vars;
    }

    /// <summary>
    /// Escapes a value for safe inclusion in a JSON string context.
    /// </summary>
    private static string EscapeJsonValue(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex PlaceholderRegex();
}
