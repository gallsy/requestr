using System.Text.Json;

namespace Requestr.Core.Utilities;

/// <summary>
/// Parses the supported dropdown-option formats into normalized value/text pairs.
/// </summary>
public static class DropdownOptionParser
{
    public static List<(string Value, string Text)> Parse(string? dropdownOptions)
    {
        if (string.IsNullOrWhiteSpace(dropdownOptions))
            return new List<(string Value, string Text)>();

        try
        {
            var simpleOptions = JsonSerializer.Deserialize<string[]>(dropdownOptions);
            if (simpleOptions != null)
            {
                return simpleOptions.Select(option => (option, option)).ToList();
            }
        }
        catch (JsonException)
        {
            // Try the value/text object format next.
        }

        try
        {
            var complexOptions = JsonSerializer.Deserialize<List<DropdownOption>>(dropdownOptions);
            if (complexOptions != null)
            {
                return complexOptions
                    .Select(option => (option.Value, option.Text))
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Fall back to the form builder's line-separated format.
        }

        if (dropdownOptions.Contains('\n') || dropdownOptions.Contains('\r'))
        {
            return dropdownOptions
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(option => option.Trim())
                .Where(option => option.Length > 0)
                .Select(option => (option, option))
                .ToList();
        }

        return new List<(string Value, string Text)> { (dropdownOptions, dropdownOptions) };
    }

    private sealed class DropdownOption
    {
        public string Value { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
