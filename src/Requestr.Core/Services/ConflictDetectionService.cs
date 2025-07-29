using System.Text.Json;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;

namespace Requestr.Core.Services;

public class ConflictDetectionService : IConflictDetectionService
{
    private readonly IDataService _dataService;
    private readonly IFormRequestService _formRequestService;
    private readonly IBulkFormRequestService _bulkFormRequestService;
    private readonly ILogger<ConflictDetectionService> _logger;

    public ConflictDetectionService(
        IDataService dataService, 
        IFormRequestService formRequestService,
        IBulkFormRequestService bulkFormRequestService,
        ILogger<ConflictDetectionService> logger)
    {
        _dataService = dataService;
        _formRequestService = formRequestService;
        _bulkFormRequestService = bulkFormRequestService;
        _logger = logger;
    }

    public async Task<ConflictDetectionResult> CheckForConflictsAsync(FormRequest formRequest)
    {
        var result = new ConflictDetectionResult { FormRequestId = formRequest.Id };

        if (formRequest.FormDefinition == null)
        {
            return result;
        }

        // Only check conflicts for Update and Delete operations that have original values
        if (formRequest.RequestType != RequestType.Update && formRequest.RequestType != RequestType.Delete)
        {
            return result;
        }

        if (!formRequest.OriginalValues.Any())
        {
            return result;
        }

        try
        {
            var formDefinition = formRequest.FormDefinition;
            
            // Get primary key columns
            var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                formDefinition.DatabaseConnectionName,
                formDefinition.TableName,
                formDefinition.Schema);

            if (!primaryKeyColumns.Any())
            {
                // Can't check conflicts without primary keys
                return result;
            }

            // Build WHERE conditions using primary key values from original values
            var whereConditions = new Dictionary<string, object?>();
            foreach (var pkColumn in primaryKeyColumns)
            {
                if (formRequest.OriginalValues.ContainsKey(pkColumn))
                {
                    var originalValue = formRequest.OriginalValues[pkColumn];
                    
                    // Convert JsonElement to proper type for Dapper
                    var convertedValue = ConvertJsonElementToValue(originalValue);
                    whereConditions[pkColumn] = convertedValue;
                }
                else
                {
                    // Missing primary key in original values - can't check conflicts
                    return result;
                }
            }

            // Query current database values
            var currentRecords = await _dataService.QueryDataAsync(
                formDefinition.DatabaseConnectionName,
                formDefinition.TableName,
                formDefinition.Schema,
                whereConditions);

            if (currentRecords.Count == 0)
            {
                result.HasConflicts = true;
                result.ConflictMessages.Add("The record no longer exists in the database. It may have been deleted by another user.");
                return result;
            }

            if (currentRecords.Count > 1)
            {
                result.HasConflicts = true;
                result.ConflictMessages.Add("Multiple records found with the same primary key. Database integrity issue detected.");
                return result;
            }

            var currentRecord = currentRecords.First();

            // Compare stored original values with current database values
            var conflictDetails = new List<string>();
            foreach (var originalValue in formRequest.OriginalValues)
            {
                var fieldName = originalValue.Key;
                var storedOriginalValue = originalValue.Value;
                
                if (currentRecord.ContainsKey(fieldName))
                {
                    var currentValue = currentRecord[fieldName];
                    
                    // Compare values (handle nulls and type differences)
                    if (!AreValuesEqual(storedOriginalValue, currentValue))
                    {
                        // Get the new value that the request wants to set
                        var newValue = formRequest.FieldValues.ContainsKey(fieldName) 
                            ? formRequest.FieldValues[fieldName] 
                            : null;

                        // Format the conflict message with actual values
                        var originalDisplay = FormatValueForDisplay(storedOriginalValue);
                        var currentDisplay = FormatValueForDisplay(currentValue);
                        var newDisplay = FormatValueForDisplay(newValue);

                        if (formRequest.RequestType == RequestType.Update)
                        {
                            conflictDetails.Add($"• {fieldName}: Original was '{originalDisplay}', now '{currentDisplay}' in database, request wants to change to '{newDisplay}'");
                        }
                        else if (formRequest.RequestType == RequestType.Delete)
                        {
                            conflictDetails.Add($"• {fieldName}: Original was '{originalDisplay}', now '{currentDisplay}' in database");
                        }
                    }
                }
            }

            if (conflictDetails.Any())
            {
                result.HasConflicts = true;
                result.ConflictMessages.Add("The following fields have been modified by another user since this request was created:");
                result.ConflictMessages.AddRange(conflictDetails);
                result.ConflictMessages.Add("Please review the changes carefully before approving.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for conflicts in request {RequestId}", formRequest.Id);
            // Don't show conflicts if we can't check them - fail gracefully
        }

        return result;
    }

    public async Task<List<ConflictDetectionResult>> CheckBulkRequestConflictsAsync(BulkFormRequest bulkRequest)
    {
        var results = new List<ConflictDetectionResult>();

        if (bulkRequest.Items == null || !bulkRequest.Items.Any())
        {
            return results;
        }

        // Check conflicts for each individual item in the bulk request
        foreach (var item in bulkRequest.Items)
        {
            // Create a temporary FormRequest-like object for individual conflict checking
            var tempRequest = new FormRequest
            {
                Id = 0, // Bulk items don't have individual request IDs
                FormDefinitionId = bulkRequest.FormDefinitionId,
                FormDefinition = bulkRequest.FormDefinition,
                RequestType = bulkRequest.RequestType,
                FieldValues = item.FieldValues ?? new Dictionary<string, object?>(),
                OriginalValues = item.OriginalValues ?? new Dictionary<string, object?>()
            };

            var conflictResult = await CheckForConflictsAsync(tempRequest);
            
            // Add item identifier to the result for better tracking
            if (conflictResult.HasConflicts)
            {
                conflictResult.ConflictMessages.Insert(0, $"Row {item.RowNumber}:");
            }
            
            results.Add(conflictResult);
        }

        return results;
    }

    public async Task<ConflictDetectionResult> CheckForConflictsAsync(int formRequestId)
    {
        try
        {
            var formRequest = await _formRequestService.GetByIdAsync(formRequestId);
            if (formRequest == null)
            {
                return new ConflictDetectionResult
                {
                    HasConflicts = false,
                    FormRequestId = formRequestId
                };
            }

            return await CheckForConflictsAsync(formRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for conflicts in request {RequestId}", formRequestId);
            return new ConflictDetectionResult
            {
                HasConflicts = false,
                FormRequestId = formRequestId
            };
        }
    }

    public async Task<ConflictDetectionResult> CheckBulkRequestConflictsAsync(int bulkRequestId)
    {
        try
        {
            var bulkRequest = await _bulkFormRequestService.GetBulkFormRequestByIdAsync(bulkRequestId);
            if (bulkRequest == null)
            {
                return new ConflictDetectionResult
                {
                    HasConflicts = false
                };
            }

            var results = await CheckBulkRequestConflictsAsync(bulkRequest);
            
            // Combine all results into a single result
            var combinedResult = new ConflictDetectionResult();
            foreach (var result in results)
            {
                if (result.HasConflicts)
                {
                    combinedResult.HasConflicts = true;
                    combinedResult.ConflictMessages.AddRange(result.ConflictMessages);
                }
            }

            return combinedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for conflicts in bulk request {BulkRequestId}", bulkRequestId);
            return new ConflictDetectionResult
            {
                HasConflicts = false
            };
        }
    }

    private object? ConvertJsonElementToValue(object? value)
    {
        if (value == null) return null;
        
        // If it's already not a JsonElement, return as-is
        if (value is not JsonElement jsonElement)
            return value;
            
        // Convert JsonElement to appropriate type
        return jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement.TryGetInt32(out var intVal) ? intVal : jsonElement.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => jsonElement.ToString()
        };
    }

    private bool AreValuesEqual(object? value1, object? value2)
    {
        // Handle nulls
        if (value1 == null && value2 == null) return true;
        if (value1 == null || value2 == null) return false;

        // Handle DBNull
        if (value1 == DBNull.Value) value1 = null;
        if (value2 == DBNull.Value) value2 = null;
        if (value1 == null && value2 == null) return true;
        if (value1 == null || value2 == null) return false;

        // Convert to strings for comparison to handle type differences
        var str1 = value1.ToString()?.Trim();
        var str2 = value2.ToString()?.Trim();

        return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
    }

    private string FormatValueForDisplay(object? value)
    {
        if (value == null || value == DBNull.Value)
            return "(null)";
        
        var stringValue = value.ToString()?.Trim() ?? "";
        
        // Show empty strings as "(empty)"
        if (string.IsNullOrEmpty(stringValue))
            return "(empty)";
            
        // Truncate very long values for display
        if (stringValue.Length > 50)
            return stringValue.Substring(0, 47) + "...";
            
        return stringValue;
    }
}
