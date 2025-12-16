using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using System.Web;
using Requestr.Core.Models;

namespace Requestr.Core.Validation;

public static class InputValidator
{
    // Common regex patterns for validation
    private static readonly Regex EmailRegex = new(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"^[\+]?[1-9][\d]{0,15}$", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"^https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{1,256}\.[a-zA-Z0-9()]{1,6}\b([-a-zA-Z0-9()@:%_\+.~#?&//=]*)$", RegexOptions.Compiled);
    private static readonly Regex SqlInjectionPattern = new(@"(\b(ALTER|CREATE|DELETE|DROP|EXEC(UTE)?|INSERT( +INTO)?|MERGE|SELECT|UPDATE|UNION( +ALL)?)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex XssPattern = new(@"<script[^>]*(?:>.*?</script>|/>)|<iframe[^>]*(?:>.*?</iframe>|/>)|<object[^>]*(?:>.*?</object>|/>)|<embed[^>]*(?:>.*?</embed>|/>)|<link[^>]*(?:>.*?</link>|/>)|<meta[^>]*(?:>.*?</meta>|/>)|javascript:|vbscript:|data:text/html|onload=|onerror=|onclick=|onmouseover=|onmouseout=|onkeydown=|onkeyup=|onfocus=|onblur=|onchange=|onsubmit=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Dangerous characters that should be sanitized
    private static readonly char[] DangerousChars = { '<', '>', '"', '\'', '&', '%', '$', '(', ')', '*', '+', ',', '-', '.', '/', ':', ';', '=', '?', '@', '[', '\\', ']', '^', '`', '{', '|', '}', '~' };

    /// <summary>
    /// Validates and sanitizes input based on field type and constraints
    /// </summary>
    public static Models.ValidationResult ValidateInput(string? input, FormField field)
    {
        var result = new Models.ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(input))
        {
            if (field.IsRequired)
            {
                result.IsValid = false;
                result.Errors.Add($"{field.DisplayName} is required.");
            }
            return result;
        }

        // Check for SQL injection patterns
        if (ContainsSqlInjectionPatterns(input))
        {
            result.IsValid = false;
            result.Errors.Add($"{field.DisplayName} contains potentially dangerous content.");
            return result;
        }

        // Check for XSS patterns
        if (ContainsXssPatterns(input))
        {
            result.IsValid = false;
            result.Errors.Add($"{field.DisplayName} contains potentially dangerous script content.");
            return result;
        }

        // Length validation
        if (field.MaxLength > 0 && input.Length > field.MaxLength)
        {
            result.IsValid = false;
            result.Errors.Add($"{field.DisplayName} must be no longer than {field.MaxLength} characters.");
            return result;
        }

        // Data type specific validation
        switch (field.DataType?.ToLower())
        {
            case "email":
                if (!IsValidEmail(input))
                {
                    result.IsValid = false;
                    result.Errors.Add($"{field.DisplayName} must be a valid email address.");
                }
                break;

            case "url":
                if (!IsValidUrl(input))
                {
                    result.IsValid = false;
                    result.Errors.Add($"{field.DisplayName} must be a valid URL.");
                }
                break;

            case "phone":
            case "tel":
                if (!IsValidPhone(input))
                {
                    result.IsValid = false;
                    result.Errors.Add($"{field.DisplayName} must be a valid phone number.");
                }
                break;

            case "int":
            case "integer":
                if (!int.TryParse(input, out _))
                {
                    result.IsValid = false;
                    result.Errors.Add($"{field.DisplayName} must be a valid integer.");
                }
                break;

            case "decimal":
            case "float":
            case "double":
                if (!decimal.TryParse(input, out _))
                {
                    result.IsValid = false;
                    result.Errors.Add($"{field.DisplayName} must be a valid number.");
                }
                break;

            case "date":
                if (!DateTime.TryParse(input, out _))
                {
                    result.IsValid = false;
                    result.Errors.Add($"{field.DisplayName} must be a valid date.");
                }
                break;

            case "datetime":
            case "datetime2":
                if (!DateTime.TryParse(input, out _))
                {
                    result.IsValid = false;
                    result.Errors.Add($"{field.DisplayName} must be a valid date and time.");
                }
                break;
        }

        // Custom regex validation
        if (!string.IsNullOrWhiteSpace(field.ValidationRegex))
        {
            try
            {
                var regex = new Regex(field.ValidationRegex);
                if (!regex.IsMatch(input))
                {
                    result.IsValid = false;
                    result.Errors.Add(field.ValidationMessage ?? $"{field.DisplayName} format is invalid.");
                }
            }
            catch (ArgumentException)
            {
                result.Warnings.Add($"Invalid regex pattern for {field.DisplayName}. Regex validation skipped.");
            }
        }

        return result;
    }

    /// <summary>
    /// Sanitizes input to prevent XSS attacks while preserving legitimate content
    /// </summary>
    public static string SanitizeInput(string? input, FormField field)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // For text fields that might contain user content, apply HTML encoding
        var sanitized = field.DataType?.ToLower() switch
        {
            "text" or "textarea" or "ntext" or "varchar" or "nvarchar" => SanitizeText(input),
            "html" => SanitizeHtml(input), // For rich text fields if implemented
            _ => HttpUtility.HtmlEncode(input) // Default encoding for all other types
        };

        return sanitized;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email && EmailRegex.IsMatch(email);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var result) 
               && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps)
               && UrlRegex.IsMatch(url);
    }

    private static bool IsValidPhone(string phone)
    {
        var cleaned = Regex.Replace(phone, @"[\s\-\(\)]", "");
        return PhoneRegex.IsMatch(cleaned);
    }

    private static bool ContainsSqlInjectionPatterns(string input)
    {
        return SqlInjectionPattern.IsMatch(input);
    }

    private static bool ContainsXssPatterns(string input)
    {
        return XssPattern.IsMatch(input);
    }

    private static string SanitizeText(string input)
    {
        // HTML encode to prevent XSS
        var sanitized = HttpUtility.HtmlEncode(input);
        
        // Remove or escape dangerous characters that might have survived encoding
        sanitized = sanitized.Replace("&#60;script", "&amp;#60;script") // Double encode script tags
                            .Replace("javascript:", "java-script:")      // Break javascript protocol
                            .Replace("vbscript:", "vb-script:");         // Break vbscript protocol

        return sanitized;
    }

    private static string SanitizeHtml(string input)
    {
        // For future implementation of rich text fields
        // This would use a library like HtmlSanitizer to allow safe HTML while removing dangerous elements
        // For now, just encode everything
        return HttpUtility.HtmlEncode(input);
    }

    /// <summary>
    /// Validates that a string doesn't contain an excessive number of dangerous characters
    /// </summary>
    public static bool HasExcessiveDangerousCharacters(string input, double threshold = 0.3)
    {
        if (string.IsNullOrEmpty(input) || input.Length < 10)
            return false;

        var dangerousCount = input.Count(c => DangerousChars.Contains(c));
        var ratio = (double)dangerousCount / input.Length;
        
        return ratio > threshold;
    }

    /// <summary>
    /// Validates file upload content type and size
    /// </summary>
    public static Models.ValidationResult ValidateFileUpload(string fileName, long fileSize, string contentType)
    {
        var result = new Models.ValidationResult { IsValid = true };

        // File size validation (10MB limit)
        const long maxFileSize = 10 * 1024 * 1024;
        if (fileSize > maxFileSize)
        {
            result.IsValid = false;
            result.Errors.Add($"File size ({fileSize / 1024 / 1024} MB) exceeds the maximum limit of 10 MB.");
        }

        // File extension validation
        var allowedExtensions = new[] { ".csv" };
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension))
        {
            result.IsValid = false;
            result.Errors.Add($"File type '{extension}' is not allowed. Only CSV files are permitted.");
        }

        // Content type validation
        var allowedContentTypes = new[] { "text/csv", "application/csv", "text/plain" };
        if (!allowedContentTypes.Contains(contentType.ToLowerInvariant()))
        {
            result.IsValid = false;
            result.Errors.Add($"Content type '{contentType}' is not allowed for CSV files.");
        }

        return result;
    }
}
