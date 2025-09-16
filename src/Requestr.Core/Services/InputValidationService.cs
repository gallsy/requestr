using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Validation;

namespace Requestr.Core.Services;

public interface IInputValidationService
{
    /// <summary>
    /// Validates and sanitizes form field input
    /// </summary>
    Task<(bool IsValid, string SanitizedValue, List<string> Errors)> ValidateAndSanitizeFieldAsync(string? input, FormField field);
    
    /// <summary>
    /// Validates form submission data
    /// </summary>
    Task<ValidationResult> ValidateFormSubmissionAsync(Dictionary<string, object?> fieldValues, List<FormField> formFields);
    
    /// <summary>
    /// Validates CSV upload content
    /// </summary>
    Task<ValidationResult> ValidateCsvUploadAsync(Stream csvStream, string fileName, long fileSize, string contentType);
    
    /// <summary>
    /// Sanitizes comments and text content
    /// </summary>
    string SanitizeComments(string? comments);
}

public class InputValidationService : IInputValidationService
{
    private readonly ILogger<InputValidationService> _logger;

    public InputValidationService(ILogger<InputValidationService> logger)
    {
        _logger = logger;
    }

    public Task<(bool IsValid, string SanitizedValue, List<string> Errors)> ValidateAndSanitizeFieldAsync(string? input, FormField field)
    {
        try
        {
            var validationResult = InputValidator.ValidateInput(input, field);
            var sanitizedValue = InputValidator.SanitizeInput(input, field);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Validation failed for field {FieldName} with input: {Input}. Errors: {Errors}", 
                    field.Name, input?.Substring(0, Math.Min(50, input?.Length ?? 0)), string.Join(", ", validationResult.Errors));
            }

            return Task.FromResult((validationResult.IsValid, sanitizedValue, validationResult.Errors));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating field {FieldName} with input: {Input}", field.Name, input);
            return Task.FromResult((false, string.Empty, new List<string> { "Validation error occurred" }));
        }
    }

    public Task<ValidationResult> ValidateFormSubmissionAsync(Dictionary<string, object?> fieldValues, List<FormField> formFields)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            foreach (var field in formFields.Where(f => f.IsVisible))
            {
                var inputValue = fieldValues.ContainsKey(field.Name) ? fieldValues[field.Name]?.ToString() : null;
                
                var fieldValidation = InputValidator.ValidateInput(inputValue, field);
                if (!fieldValidation.IsValid)
                {
                    result.IsValid = false;
                    result.Errors.AddRange(fieldValidation.Errors);
                }

                result.Warnings.AddRange(fieldValidation.Warnings);

                // Additional security checks for potentially dangerous input
                if (!string.IsNullOrEmpty(inputValue))
                {
                    if (InputValidator.HasExcessiveDangerousCharacters(inputValue))
                    {
                        result.IsValid = false;
                        result.Errors.Add($"{field.DisplayName} contains an excessive number of special characters.");
                        
                        _logger.LogWarning("Potentially malicious input detected in field {FieldName}: {Input}", 
                            field.Name, inputValue.Substring(0, Math.Min(100, inputValue.Length)));
                    }
                }
            }

            if (!result.IsValid)
            {
                _logger.LogWarning("Form submission validation failed with {ErrorCount} errors", result.Errors.Count);
            }

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during form submission validation");
            result.IsValid = false;
            result.Errors.Add("Validation error occurred");
            return Task.FromResult(result);
        }
    }

    public async Task<ValidationResult> ValidateCsvUploadAsync(Stream csvStream, string fileName, long fileSize, string contentType)
    {
        try
        {
            // First validate file properties
            var fileValidation = InputValidator.ValidateFileUpload(fileName, fileSize, contentType);
            if (!fileValidation.IsValid)
            {
                return fileValidation;
            }

            // Read and validate CSV content (handle non-seekable streams e.g. BrowserFileStream)
            if (csvStream.CanSeek)
            {
                csvStream.Position = 0;
            }

            string content;
            if (!csvStream.CanSeek)
            {
                using var tempMs = new MemoryStream();
                await csvStream.CopyToAsync(tempMs);
                tempMs.Position = 0;
                using var tempReader = new StreamReader(tempMs, leaveOpen: true);
                content = await tempReader.ReadToEndAsync();
            }
            else
            {
                using var reader = new StreamReader(csvStream, leaveOpen: true);
                content = await reader.ReadToEndAsync();
            }
            
            var contentValidation = InputValidator.ValidateCsvContent(content);
            
            if (!contentValidation.IsValid)
            {
                _logger.LogWarning("CSV upload validation failed for file {FileName}. Errors: {Errors}", 
                    fileName, string.Join(", ", contentValidation.Errors));
            }

            // Reset stream position for subsequent processing if possible
            if (csvStream.CanSeek)
            {
                csvStream.Position = 0;
            }
            
            return contentValidation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating CSV upload for file {FileName}", fileName);
            return ValidationResult.Failure("Error validating CSV file");
        }
    }

    public string SanitizeComments(string? comments)
    {
        if (string.IsNullOrWhiteSpace(comments))
            return string.Empty;

        try
        {
            // Create a dummy field for comment validation
            var commentField = new FormField
            {
                Name = "Comments",
                DataType = "textarea",
                MaxLength = 1000,
                DisplayName = "Comments"
            };

            return InputValidator.SanitizeInput(comments, commentField);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing comments: {Comments}", comments.Substring(0, Math.Min(50, comments.Length)));
            return string.Empty;
        }
    }
}
