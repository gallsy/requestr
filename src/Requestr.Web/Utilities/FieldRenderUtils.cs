using System.Text.Json;
using Requestr.Core.Models;

namespace Requestr.Web.Utilities;

public static class FieldRenderUtils
{
    public static string DetermineControlType(FormField field)
    {
        var dataType = field.DataType?.ToLower() ?? "";
        var controlType = field.ControlType?.ToLower();
        if (!string.IsNullOrEmpty(controlType)) return controlType;

        return dataType switch
        {
            "bit" => "checkbox",
            "date" => "date",
            "datetime" or "datetime2" or "smalldatetime" => "datetime-local",
            "time" => "time",
            "text" or "ntext" => "textarea",
            _ => IsLongText(dataType) ? "textarea" : "input"
        };
    }

    public static string GetInputType(string dataType)
    {
        var lowerType = dataType?.ToLower() ?? "text";
        return lowerType switch
        {
            // HTML-native types
            "number" or "date" or "datetime-local" or "time" or "email" or "text" => lowerType,
            // SQL types mapped to input
            "bit" => "checkbox",
            "tinyint" or "smallint" or "int" or "bigint" => "number",
            "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney" => "number",
            "datetime" or "datetime2" or "smalldatetime" => "datetime-local",
            _ => "text"
        };
    }

    public static List<(string Value, string Text)> GetDropdownOptions(string? dropdownOptionsJson)
    {
        var list = new List<(string Value, string Text)>();
        if (string.IsNullOrWhiteSpace(dropdownOptionsJson)) return list;

        try
        {
            var simpleOptions = JsonSerializer.Deserialize<string[]>(dropdownOptionsJson);
            if (simpleOptions != null)
            {
                return simpleOptions.Select(o => (o, o)).ToList();
            }
        }
        catch
        {
            try
            {
                var complex = JsonSerializer.Deserialize<List<DropdownOption>>(dropdownOptionsJson);
                if (complex != null)
                {
                    return complex.Select(o => (o.Value, o.Text)).ToList();
                }
            }
            catch
            {
                // Fallback: line-separated values
                if (dropdownOptionsJson.Contains('\n') || dropdownOptionsJson.Contains('\r'))
                {
                    var lines = dropdownOptionsJson.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    return lines.Select(l => (l.Trim(), l.Trim())).ToList();
                }
                return new List<(string, string)> { (dropdownOptionsJson, dropdownOptionsJson) };
            }
        }
        return list;
    }

    public static object? ConvertStringToTyped(string dataType, string controlType, string value)
    {
        var dt = dataType?.ToLower() ?? string.Empty;
        var ct = controlType?.ToLower() ?? string.Empty;
        var s = value?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(s)) return string.Empty; // treat empty as empty string unless caller handles nulls explicitly

        if (ct == "checkbox" || dt == "bit")
        {
            if (bool.TryParse(s, out var b)) return b;
            if (s == "1") return true;
            if (s == "0") return false;
            return false;
        }

        // Numeric
        if (dt is "tinyint" or "smallint" or "int")
        {
            if (int.TryParse(s, out var iv)) return iv; return s;
        }
        if (dt == "bigint")
        {
            if (long.TryParse(s, out var lv)) return lv; return s;
        }
        if (dt is "decimal" or "numeric" or "money" or "smallmoney")
        {
            if (decimal.TryParse(s, out var dv)) return dv; return s;
        }
        if (dt is "float" or "real")
        {
            if (double.TryParse(s, out var fv)) return fv; return s;
        }

        // Date/Time
        if (ct == "date" || dt == "date")
        {
            if (DateTime.TryParse(s, out var d)) return d.Date;
            return s;
        }
        if (ct == "datetime-local" || dt is "datetime" or "datetime2" or "smalldatetime")
        {
            if (DateTime.TryParse(s, out var dtVal)) return dtVal;
            return s;
        }
        if (ct == "time" || dt == "time")
        {
            if (TimeSpan.TryParse(s, out var t)) return t;
            if (DateTime.TryParse(s, out var dtt)) return dtt.TimeOfDay;
            return s;
        }

        return s; // default to string
    }

    private static bool IsLongText(string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType)) return false;
        var dt = dataType.ToLower();
        if (dt.Contains("varchar") || dt.Contains("nvarchar") || dt.Contains("char") || dt.Contains("nchar"))
        {
            var open = dataType.IndexOf('(');
            var close = dataType.IndexOf(')');
            if (open > 0 && close > open + 1)
            {
                var lenStr = dataType.Substring(open + 1, close - open - 1);
                if (int.TryParse(lenStr, out var len))
                {
                    return len > 255;
                }
            }
        }
        return false;
    }

    public class DropdownOption
    {
        public string Value { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
