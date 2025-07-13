using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;
using System.Text.Json;

namespace Requestr.Core.Services;

public class BulkFormRequestService : IBulkFormRequestService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BulkFormRequestService> _logger;
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly IFormRequestService _formRequestService;
    private readonly string _connectionString;

    public BulkFormRequestService(
        IConfiguration configuration,
        ILogger<BulkFormRequestService> logger,
        IFormDefinitionService formDefinitionService,
        IFormRequestService formRequestService)
    {
        _configuration = configuration;
        _logger = logger;
        _formDefinitionService = formDefinitionService;
        _formRequestService = formRequestService;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
    }

    public async Task<CsvUploadResult> ProcessCsvUploadAsync(int formDefinitionId, Stream csvStream, string fileName)
    {
        var result = new CsvUploadResult();
        
        try
        {
            // Get form definition to validate against
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formDefinitionId);
            if (formDefinition == null)
            {
                result.Errors.Add("Form definition not found");
                return result;
            }

            // Configure CSV reader
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null,
                TrimOptions = TrimOptions.Trim
            };

            using var reader = new StreamReader(csvStream, Encoding.UTF8);
            using var csv = new CsvReader(reader, config);

            // Read headers
            await csv.ReadAsync();
            csv.ReadHeader();
            var headers = csv.HeaderRecord;

            if (headers == null || !headers.Any())
            {
                result.Errors.Add("CSV file must contain headers");
                return result;
            }

            // Validate headers against form fields
            var validationErrors = ValidateHeaders(headers, formDefinition.Fields);
            if (validationErrors.Any())
            {
                result.Errors.AddRange(validationErrors);
                return result;
            }

            // Process each row
            var rowNumber = 1; // Start from 1 (header is row 0)
            while (await csv.ReadAsync())
            {
                rowNumber++;
                var rowValidation = await ValidateRowAsync(csv, formDefinition, rowNumber);
                result.ValidationResults.Add(rowValidation);
                
                if (rowValidation.IsValid)
                {
                    result.ParsedData.Add(rowValidation.ParsedData);
                    result.ValidRows++;
                }
                else
                {
                    result.InvalidRows++;
                }
            }

            result.TotalRows = result.ValidRows + result.InvalidRows;
            result.IsValid = result.InvalidRows == 0 && result.ValidRows > 0;

            return result;            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CSV file");
                result.Errors.Add($"Error processing CSV file: {ex.Message}");
                return result;
            }
    }

    private List<string> ValidateHeaders(string[] headers, List<FormField> formFields)
    {
        var errors = new List<string>();
        var formFieldNames = formFields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Check for required fields
        var requiredFields = formFields.Where(f => f.IsRequired && f.IsVisible).Select(f => f.Name).ToList();
        var missingRequired = requiredFields.Where(rf => !headers.Contains(rf, StringComparer.OrdinalIgnoreCase)).ToList();
        
        if (missingRequired.Any())
        {
            errors.Add($"Missing required columns: {string.Join(", ", missingRequired)}");
        }

        // Check for unknown columns
        var unknownColumns = headers.Where(h => !formFieldNames.Contains(h)).ToList();
        if (unknownColumns.Any())
        {
            errors.Add($"Unknown columns: {string.Join(", ", unknownColumns)}");
        }

        return errors;
    }

    private async Task<CsvRowValidationResult> ValidateRowAsync(CsvReader csv, FormDefinition formDefinition, int rowNumber)
    {
        var result = new CsvRowValidationResult
        {
            RowNumber = rowNumber,
            IsValid = true
        };

        try
        {
            foreach (var field in formDefinition.Fields.Where(f => f.IsVisible))
            {
                var cellValue = csv.GetField(field.Name);
                
                // Validate required fields
                if (field.IsRequired && string.IsNullOrWhiteSpace(cellValue))
                {
                    result.Errors.Add($"Column '{field.Name}' is required");
                    result.IsValid = false;
                    continue;
                }

                // Convert and validate data type
                var convertedValue = await ConvertAndValidateFieldValueAsync(cellValue, field, result);
                if (convertedValue != null || !field.IsRequired)
                {
                    result.ParsedData[field.Name] = convertedValue;
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error processing row: {ex.Message}");
            result.IsValid = false;
        }

        return result;
    }

    private Task<object?> ConvertAndValidateFieldValueAsync(string? cellValue, FormField field, CsvRowValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(cellValue))
        {
            return Task.FromResult<object?>(null);
        }

        try
        {
            object? convertedValue = field.DataType?.ToLower() switch
            {
                "int" or "integer" => int.Parse(cellValue),
                "decimal" or "float" or "double" => decimal.Parse(cellValue),
                "bool" or "boolean" => bool.Parse(cellValue),
                "datetime" => DateTime.Parse(cellValue),
                "date" => DateTime.Parse(cellValue).Date,
                _ => (object?)cellValue
            };
            
            return Task.FromResult(convertedValue);
        }
        catch (Exception)
        {
            result.Errors.Add($"Invalid {field.DataType} value in column '{field.Name}': {cellValue}");
            result.IsValid = false;
            return Task.FromResult<object?>(null);
        }
    }

    public async Task<BulkFormRequest> CreateBulkFormRequestAsync(CreateBulkFormRequestDto createDto, string userId, string userName)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var bulkRequest = new BulkFormRequest
            {
                FormDefinitionId = createDto.FormDefinitionId,
                RequestType = createDto.RequestType,
                FileName = createDto.FileName,
                TotalRows = createDto.FormRequests.Count,
                ValidRows = createDto.FormRequests.Count,
                InvalidRows = 0,
                SelectedRows = createDto.FormRequests.Count,
                RequestedBy = userId,
                RequestedByName = userName,
                RequestedAt = DateTime.UtcNow,
                Comments = createDto.Comments,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            // Create the bulk request
            var sql = @"
                INSERT INTO BulkFormRequests (FormDefinitionId, RequestType, FileName, TotalRows, ValidRows, InvalidRows, SelectedRows, 
                                            RequestedBy, RequestedByName, RequestedAt, Comments, CreatedBy, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@FormDefinitionId, @RequestType, @FileName, @TotalRows, @ValidRows, @InvalidRows, @SelectedRows,
                        @RequestedBy, @RequestedByName, @RequestedAt, @Comments, @CreatedBy, @CreatedAt)";

            var bulkRequestId = await connection.QuerySingleAsync<int>(sql, new
            {
                FormDefinitionId = bulkRequest.FormDefinitionId,
                RequestType = (int)bulkRequest.RequestType,
                FileName = bulkRequest.FileName,
                TotalRows = bulkRequest.TotalRows,
                ValidRows = bulkRequest.ValidRows,
                InvalidRows = bulkRequest.InvalidRows,
                SelectedRows = bulkRequest.SelectedRows,
                RequestedBy = bulkRequest.RequestedBy,
                RequestedByName = bulkRequest.RequestedByName,
                RequestedAt = bulkRequest.RequestedAt,
                Comments = bulkRequest.Comments,
                CreatedBy = bulkRequest.CreatedBy,
                CreatedAt = bulkRequest.CreatedAt
            }, transaction);

            bulkRequest.Id = bulkRequestId;

            // Create individual form requests using the existing service
            foreach (var formRequestDto in createDto.FormRequests)
            {
                var formRequest = new FormRequest
                {
                    FormDefinitionId = formRequestDto.FormDefinitionId,
                    RequestType = formRequestDto.RequestType,
                    FieldValues = formRequestDto.FieldValues,
                    OriginalValues = formRequestDto.OriginalValues,
                    RequestedBy = userId,
                    RequestedByName = userName,
                    RequestedAt = DateTime.UtcNow,
                    Comments = formRequestDto.Comments,
                    BulkFormRequestId = bulkRequestId
                };

                // We need to create the form request with the bulk ID
                var createFormRequestSql = @"
                    INSERT INTO FormRequests (FormDefinitionId, RequestType, FieldValues, OriginalValues, Status, 
                                            RequestedBy, RequestedByName, RequestedAt, Comments, BulkFormRequestId)
                    OUTPUT INSERTED.Id
                    VALUES (@FormDefinitionId, @RequestType, @FieldValues, @OriginalValues, @Status,
                            @RequestedBy, @RequestedByName, @RequestedAt, @Comments, @BulkFormRequestId)";

                var formRequestId = await connection.QuerySingleAsync<int>(createFormRequestSql, new
                {
                    FormDefinitionId = formRequest.FormDefinitionId,
                    RequestType = (int)formRequest.RequestType,
                    FieldValues = JsonSerializer.Serialize(formRequest.FieldValues),
                    OriginalValues = JsonSerializer.Serialize(formRequest.OriginalValues),
                    Status = (int)formRequest.Status,
                    RequestedBy = formRequest.RequestedBy,
                    RequestedByName = formRequest.RequestedByName,
                    RequestedAt = formRequest.RequestedAt,
                    Comments = formRequest.Comments,
                    BulkFormRequestId = formRequest.BulkFormRequestId
                }, transaction);

                formRequest.Id = formRequestId;
                bulkRequest.FormRequests.Add(formRequest);
            }

            await transaction.CommitAsync();
            return bulkRequest;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<BulkFormRequest?> GetBulkFormRequestByIdAsync(int id)
    {
        _logger.LogInformation($"Getting bulk form request by ID: {id}");
        
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            var sql = @"
                SELECT bfr.*
                FROM BulkFormRequests bfr
                WHERE bfr.Id = @Id";
            
            var bulkRequest = await connection.QueryFirstOrDefaultAsync<BulkFormRequest>(sql, new { Id = id });
            
            _logger.LogInformation(bulkRequest == null 
                ? $"No bulk request found with ID: {id}" 
                : $"Found bulk request. ID: {bulkRequest.Id}, FormDefinitionId: {bulkRequest.FormDefinitionId}");
            
            if (bulkRequest != null)
            {
                // Load the FormDefinition properly
                var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(bulkRequest.FormDefinitionId);
                bulkRequest.FormDefinition = formDefinition;

                _logger.LogInformation(formDefinition == null 
                    ? $"No form definition found for ID: {bulkRequest.FormDefinitionId}"
                    : $"Found form definition. ID: {formDefinition.Id}, Name: {formDefinition.Name}");

                // Parse the RequestType from string
                var requestTypeResult = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT RequestType FROM BulkFormRequests WHERE Id = @Id", new { Id = id });
                
                if (requestTypeResult != null)
                {
                    bulkRequest.RequestType = ParseRequestType(requestTypeResult);
                }

                // Load associated form requests
                var formRequestsSql = @"
                    SELECT 
                        fr.Id,
                        fr.FormDefinitionId,
                        fr.Status,
                        fr.RequestedBy,
                        fr.RequestedByName,
                        fr.RequestedAt,
                        fr.ApprovedBy,
                        fr.ApprovedByName,
                        fr.ApprovedAt,
                        fr.Comments,
                        fr.RejectionReason,
                        fr.BulkFormRequestId,
                        fr.FieldValues,
                        fr.OriginalValues,
                        fr.RequestType
                    FROM FormRequests fr
                    WHERE fr.BulkFormRequestId = @BulkFormRequestId
                    ORDER BY fr.Id";

                var formRequestResults = await connection.QueryAsync(formRequestsSql, new { BulkFormRequestId = id });
                
                var formRequests = new List<FormRequest>();
                
                // Map and deserialize each form request
                foreach (var row in formRequestResults)
                {
                    var formRequest = new FormRequest
                    {
                        Id = row.Id,
                        FormDefinitionId = row.FormDefinitionId,
                        Status = (RequestStatus)row.Status,
                        RequestedBy = row.RequestedBy,
                        RequestedByName = row.RequestedByName,
                        RequestedAt = row.RequestedAt,
                        ApprovedBy = row.ApprovedBy,
                        ApprovedByName = row.ApprovedByName,
                        ApprovedAt = row.ApprovedAt,
                        Comments = row.Comments,
                        RejectionReason = row.RejectionReason,
                        BulkFormRequestId = row.BulkFormRequestId
                    };
                    
                    // Deserialize JSON strings to dictionaries
                    if (row.FieldValues != null)
                    {
                        _logger.LogInformation($"Deserializing FieldValues: {row.FieldValues}");
                        formRequest.FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.FieldValues.ToString()) ?? new Dictionary<string, object?>();
                    }
                    else
                    {
                        formRequest.FieldValues = new Dictionary<string, object?>();
                    }
                    
                    if (row.OriginalValues != null)
                    {
                        _logger.LogInformation($"Deserializing OriginalValues: {row.OriginalValues}");
                        formRequest.OriginalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.OriginalValues.ToString()) ?? new Dictionary<string, object?>();
                    }
                    else
                    {
                        formRequest.OriginalValues = new Dictionary<string, object?>();
                    }
                    
                    // Parse RequestType from string
                    if (row.RequestType != null)
                    {
                        formRequest.RequestType = ParseRequestType(row.RequestType);
                    }
                    
                    formRequests.Add(formRequest);
                }
                
                bulkRequest.FormRequests = formRequests.ToList();
            }

            return bulkRequest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving bulk form request with ID {id}: {ex.Message}");
            throw; // Rethrow to let the calling code handle it
        }
    }

    public async Task<List<BulkFormRequest>> GetBulkFormRequestsByUserAsync(string userId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        var sql = @"
            SELECT bfr.*
            FROM BulkFormRequests bfr
            WHERE bfr.RequestedBy = @UserId
            ORDER BY bfr.RequestedAt DESC";

        var results = await connection.QueryAsync<BulkFormRequest>(sql, new { UserId = userId });
        
        // Load FormDefinition and parse RequestType for each result
        foreach (var result in results)
        {
            // Load FormDefinition
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(result.FormDefinitionId);
            result.FormDefinition = formDefinition;
            
            // Parse RequestType
            var requestType = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT RequestType FROM BulkFormRequests WHERE Id = @Id", new { Id = result.Id });
            if (requestType != null)
            {
                result.RequestType = ParseRequestType(requestType);
            }
            
            // Load FormRequests count for display
            var formRequestsCount = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM FormRequests WHERE BulkFormRequestId = @Id", new { Id = result.Id });
            
            // Note: We're not fully loading FormRequests here for performance, just getting the count
            // The FormRequests will be empty, but we know the count for display purposes
            result.FormRequests = new List<FormRequest>();
            result.SelectedRows = formRequestsCount; // Use SelectedRows to track actual count for display
        }
        
        return results.ToList();
    }

    public async Task<List<BulkFormRequest>> GetBulkFormRequestsForApprovalAsync(string userId, List<string> userRoles)
    {
        using var connection = new SqlConnection(_connectionString);
        
        var rolesList = string.Join(",", userRoles.Select(r => $"'{r}'"));
        var sql = @"
            SELECT bfr.*
            FROM BulkFormRequests bfr
            INNER JOIN FormDefinitions fd ON bfr.FormDefinitionId = fd.Id
            WHERE bfr.Status = 0 AND bfr.RequestedBy != @UserId
            AND EXISTS (
                SELECT 1 FROM STRING_SPLIT(fd.ApproverRoles, ',') ar
                WHERE ar.value IN (" + rolesList + @")
            )
            ORDER BY bfr.RequestedAt";

        var results = await connection.QueryAsync<BulkFormRequest>(sql, new { UserId = userId });
        
        // Load FormDefinition and parse RequestType for each result
        foreach (var result in results)
        {
            // Load FormDefinition
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(result.FormDefinitionId);
            result.FormDefinition = formDefinition;
            
            // Parse RequestType
            var requestType = await connection.QueryFirstOrDefaultAsync<string>(
                "SELECT RequestType FROM BulkFormRequests WHERE Id = @Id", new { Id = result.Id });
            if (requestType != null)
            {
                result.RequestType = ParseRequestType(requestType);
            }
            
            // Load FormRequests count for display
            var formRequestsCount = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM FormRequests WHERE BulkFormRequestId = @Id", new { Id = result.Id });
            
            result.FormRequests = new List<FormRequest>();
            result.SelectedRows = formRequestsCount; // Use SelectedRows to track actual count for display
        }
        
        return results.ToList();
    }

    public async Task<bool> ApproveBulkFormRequestAsync(int id, string userId, string userName, string? comments = null)
    {
        // Step 1: Update status of bulk request and all sub-requests in one transaction
        List<int> formRequestIds;
        bool statusUpdateSuccess = false;
        
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();
            
            try
            {
                _logger.LogInformation("Approving bulk form request ID: {Id}", id);
                
                // Update the bulk request status to approved
                var sql = @"
                    UPDATE BulkFormRequests 
                    SET Status = 1, ApprovedBy = @UserId, ApprovedByName = @UserName, ApprovedAt = @ApprovedAt,
                        Comments = CASE WHEN @Comments IS NOT NULL THEN @Comments ELSE Comments END,
                        UpdatedBy = @UserId, UpdatedAt = @ApprovedAt
                    WHERE Id = @Id AND Status = 0";

                var approvedAt = DateTime.UtcNow;
                var result = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId, UserName = userName, ApprovedAt = approvedAt, Comments = comments }, transaction);

                if (result <= 0)
                {
                    _logger.LogWarning("No bulk request with ID {Id} was updated. It may not exist or is not in a pending state.", id);
                    await transaction.RollbackAsync();
                    return false;
                }
                
                // Get all associated form requests
                var getFormRequestsSql = @"
                    SELECT Id FROM FormRequests 
                    WHERE BulkFormRequestId = @BulkFormRequestId AND Status = 0";
                    
                formRequestIds = (await connection.QueryAsync<int>(getFormRequestsSql, new { BulkFormRequestId = id }, transaction)).AsList();
                
                if (!formRequestIds.Any())
                {
                    _logger.LogInformation("No pending individual requests found for bulk request {Id}", id);
                }
                
                // Also approve all associated form requests
                var formRequestsSql = @"
                    UPDATE FormRequests 
                    SET Status = 1, ApprovedBy = @UserId, ApprovedByName = @UserName, ApprovedAt = @ApprovedAt
                    WHERE BulkFormRequestId = @BulkFormRequestId AND Status = 0";

                await connection.ExecuteAsync(formRequestsSql, new { BulkFormRequestId = id, UserId = userId, UserName = userName, ApprovedAt = approvedAt }, transaction);
                
                // Commit the transaction for status updates
                await transaction.CommitAsync();
                statusUpdateSuccess = true;
                _logger.LogInformation("Successfully updated statuses for bulk request {Id} and {Count} sub-requests", id, formRequestIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status for bulk request {Id}", id);
                await transaction.RollbackAsync();
                return false;
            }
        }
        
        // Step 2: If status updates succeeded, now apply each form request (each with its own transaction)
        if (statusUpdateSuccess && formRequestIds != null && formRequestIds.Any())
        {
            _logger.LogInformation("Now applying {Count} individual requests for bulk request {Id}", formRequestIds.Count, id);
            int successCount = 0;
            int failureCount = 0;
            
            foreach (var requestId in formRequestIds)
            {
                try
                {
                    // Call the FormRequestService to apply each form request
                    var success = await _formRequestService.ApplyFormRequestAsync(requestId);
                    if (success)
                    {
                        successCount++;
                        _logger.LogInformation("Successfully applied form request {RequestId} within bulk request {BulkId}", requestId, id);
                    }
                    else
                    {
                        failureCount++;
                        _logger.LogWarning("Failed to apply form request {RequestId} within bulk request {BulkId}", requestId, id);
                    }
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logger.LogError(ex, "Error applying form request {RequestId} within bulk request {BulkId}", requestId, id);
                    // We continue processing other requests even if one fails
                }
            }
            
            _logger.LogInformation("Bulk request {Id} application completed: {SuccessCount} succeeded, {FailureCount} failed", 
                id, successCount, failureCount);
                
            // Consider the overall operation successful if at least one request was applied successfully
            return successCount > 0;
        }
        
        return statusUpdateSuccess;
    }

    public async Task<bool> RejectBulkFormRequestAsync(int id, string userId, string userName, string rejectionReason)
    {
        using var connection = new SqlConnection(_connectionString);
        
        var sql = @"
            UPDATE BulkFormRequests 
            SET Status = 2, ApprovedBy = @UserId, ApprovedByName = @UserName, ApprovedAt = @ApprovedAt,
                RejectionReason = @RejectionReason, UpdatedBy = @UserId, UpdatedAt = @ApprovedAt
            WHERE Id = @Id AND Status = 0";

        var approvedAt = DateTime.UtcNow;
        var result = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId, UserName = userName, ApprovedAt = approvedAt, RejectionReason = rejectionReason });

        if (result > 0)
        {
            // Also reject all associated form requests
            var formRequestsSql = @"
                UPDATE FormRequests 
                SET Status = 2, ApprovedBy = @UserId, ApprovedByName = @UserName, ApprovedAt = @ApprovedAt,
                    RejectionReason = @RejectionReason
                WHERE BulkFormRequestId = @BulkFormRequestId AND Status = 0";

            await connection.ExecuteAsync(formRequestsSql, new { BulkFormRequestId = id, UserId = userId, UserName = userName, ApprovedAt = approvedAt, RejectionReason = rejectionReason });
        }

        return result > 0;
    }

    public async Task<bool> DeleteBulkFormRequestAsync(int id, string userId)
    {
        using var connection = new SqlConnection(_connectionString);
        
        var sql = @"
            DELETE FROM BulkFormRequests 
            WHERE Id = @Id AND RequestedBy = @UserId AND Status = 0";

        var result = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId });
        return result > 0;
    }

    // Helper methods for enum conversion
    private static RequestType ParseRequestType(object requestType)
    {
        // Handle both string and integer inputs for backwards compatibility during migration
        if (requestType is int intValue)
        {
            return (RequestType)intValue;
        }
        
        var stringValue = requestType?.ToString();
        return stringValue?.ToUpper() switch
        {
            "INSERT" => RequestType.Insert,
            "UPDATE" => RequestType.Update,
            "DELETE" => RequestType.Delete,
            "0" => RequestType.Insert,
            "1" => RequestType.Update,
            "2" => RequestType.Delete,
            _ => throw new ArgumentException($"Invalid RequestType: {requestType}")
        };
    }

    public async Task<List<BulkFormRequest>> GetBulkFormRequestsByFormDefinitionIdAsync(int formDefinitionId, int limit = 10)
    {
        _logger.LogInformation($"Getting bulk form requests for form definition ID: {formDefinitionId}, limit: {limit}");
        
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            var sql = @"
                SELECT TOP (@Limit) bfr.*
                FROM BulkFormRequests bfr
                WHERE bfr.FormDefinitionId = @FormDefinitionId
                ORDER BY bfr.RequestedAt DESC";

            var results = await connection.QueryAsync<BulkFormRequest>(sql, new { FormDefinitionId = formDefinitionId, Limit = limit });
            
            // Load FormDefinition and parse RequestType for each result
            foreach (var result in results)
            {
                // Load FormDefinition
                var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(result.FormDefinitionId);
                result.FormDefinition = formDefinition;
                
                // Parse RequestType
                var requestType = await connection.QueryFirstOrDefaultAsync<string>(
                    "SELECT RequestType FROM BulkFormRequests WHERE Id = @Id", new { Id = result.Id });
                if (requestType != null)
                {
                    result.RequestType = ParseRequestType(requestType);
                }
                
                // Load FormRequests count for display
                var formRequestsCount = await connection.QuerySingleOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM FormRequests WHERE BulkFormRequestId = @Id", new { Id = result.Id });
                
                // Note: We're not fully loading FormRequests here for performance, just getting the count
                result.FormRequests = new List<FormRequest>();
                result.SelectedRows = formRequestsCount; // Use SelectedRows to track actual count for display
            }
            
            _logger.LogInformation($"Retrieved {results.Count()} bulk requests for form definition ID {formDefinitionId}");
            return results.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving bulk form requests for form definition ID {formDefinitionId}: {ex.Message}");
            throw;
        }
    }
}
