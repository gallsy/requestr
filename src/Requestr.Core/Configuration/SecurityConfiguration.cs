namespace Requestr.Core.Configuration;

public class SecurityConfiguration
{
    public InputValidationSettings InputValidation { get; set; } = new();
    public FileUploadSettings FileUpload { get; set; } = new();
    public CsvSecuritySettings CsvSecurity { get; set; } = new();
}

public class InputValidationSettings
{
    /// <summary>
    /// Threshold for dangerous character ratio (0.0-1.0)
    /// </summary>
    public double DangerousCharacterThreshold { get; set; } = 0.3;
    
    /// <summary>
    /// Whether to log validation failures
    /// </summary>
    public bool LogValidationFailures { get; set; } = true;
}

public class FileUploadSettings
{
    /// <summary>
    /// Maximum file size in bytes (default 10MB)
    /// </summary>
    public long MaxFileSize { get; set; } = 10485760;
    
    /// <summary>
    /// Allowed file extensions
    /// </summary>
    public List<string> AllowedExtensions { get; set; } = new() { ".csv" };
    
    /// <summary>
    /// Allowed MIME types
    /// </summary>
    public List<string> AllowedMimeTypes { get; set; } = new() { "text/csv", "application/csv", "text/plain" };
}

public class CsvSecuritySettings
{
    /// <summary>
    /// Whether to detect CSV injection attacks
    /// </summary>
    public bool EnableCsvInjectionDetection { get; set; } = true;
    
    /// <summary>
    /// Characters that are dangerous when starting a CSV cell
    /// </summary>
    public List<string> DangerousStartCharacters { get; set; } = new() { "=", "+", "-", "@", "\t", "\r" };
}
