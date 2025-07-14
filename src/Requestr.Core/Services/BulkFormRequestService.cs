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
    private readonly IWorkflowService _workflowService;
    private readonly string _connectionString;

    public BulkFormRequestService(
        IConfiguration configuration,
        ILogger<BulkFormRequestService> logger,
        IFormDefinitionService formDefinitionService,
        IFormRequestService formRequestService,
        IWorkflowService workflowService)
    {
        _configuration = configuration;
        _logger = logger;
        _formDefinitionService = formDefinitionService;
        _formRequestService = formRequestService;
        _workflowService = workflowService;
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
            // Get form definition to check for workflow
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(createDto.FormDefinitionId);
            if (formDefinition == null)
            {
                throw new InvalidOperationException("Form definition not found");
            }

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

            // Create the bulk request first (without workflow)
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

            // Create individual bulk request items (not FormRequests)
            foreach (var formRequestDto in createDto.FormRequests)
            {
                var bulkItem = new BulkFormRequestItem
                {
                    BulkFormRequestId = bulkRequestId,
                    FieldValues = formRequestDto.FieldValues,
                    OriginalValues = formRequestDto.OriginalValues,
                    RowNumber = bulkRequest.Items.Count + 1, // Sequential row numbering
                    Status = RequestStatus.Pending
                };

                // Create the bulk request item
                var createItemSql = @"
                    INSERT INTO BulkFormRequestItems (BulkFormRequestId, FieldValues, OriginalValues, RowNumber, Status, CreatedBy)
                    OUTPUT INSERTED.Id
                    VALUES (@BulkFormRequestId, @FieldValues, @OriginalValues, @RowNumber, @Status, @CreatedBy)";

                var itemId = await connection.QuerySingleAsync<int>(createItemSql, new
                {
                    BulkFormRequestId = bulkRequestId,
                    FieldValues = JsonSerializer.Serialize(bulkItem.FieldValues),
                    OriginalValues = JsonSerializer.Serialize(bulkItem.OriginalValues),
                    RowNumber = bulkItem.RowNumber,
                    Status = (int)bulkItem.Status,
                    CreatedBy = userId
                }, transaction);

                bulkItem.Id = itemId;
                bulkRequest.Items.Add(bulkItem);
            }

            // Start workflow for bulk request if form has one
            if (formDefinition.WorkflowDefinitionId.HasValue && bulkRequest.Items.Any())
            {
                try
                {
                    // Create a temporary FormRequest for workflow initiation
                    // This is needed because the workflow system expects a FormRequest ID
                    var tempFormRequest = new FormRequest
                    {
                        FormDefinitionId = formDefinition.Id,
                        RequestType = createDto.RequestType,
                        FieldValues = new Dictionary<string, object> 
                        { 
                            ["IsBulkRequest"] = true,
                            ["BulkRequestId"] = bulkRequestId,
                            ["ItemCount"] = bulkRequest.Items.Count
                        },
                        RequestedBy = userId,
                        RequestedByName = userName,
                        RequestedAt = DateTime.UtcNow,
                        Comments = $"Workflow for bulk request #{bulkRequestId}",
                        BulkFormRequestId = bulkRequestId
                    };

                    var tempFormRequestSql = @"
                        INSERT INTO FormRequests (FormDefinitionId, RequestType, FieldValues, OriginalValues, Status, 
                                                RequestedBy, RequestedByName, RequestedAt, Comments, BulkFormRequestId)
                        OUTPUT INSERTED.Id
                        VALUES (@FormDefinitionId, @RequestType, @FieldValues, @OriginalValues, @Status,
                                @RequestedBy, @RequestedByName, @RequestedAt, @Comments, @BulkFormRequestId)";

                    var tempFormRequestId = await connection.QuerySingleAsync<int>(tempFormRequestSql, new
                    {
                        FormDefinitionId = tempFormRequest.FormDefinitionId,
                        RequestType = (int)tempFormRequest.RequestType,
                        FieldValues = JsonSerializer.Serialize(tempFormRequest.FieldValues),
                        OriginalValues = JsonSerializer.Serialize(tempFormRequest.OriginalValues ?? new Dictionary<string, object>()),
                        Status = (int)tempFormRequest.Status,
                        RequestedBy = tempFormRequest.RequestedBy,
                        RequestedByName = tempFormRequest.RequestedByName,
                        RequestedAt = tempFormRequest.RequestedAt,
                        Comments = tempFormRequest.Comments,
                        BulkFormRequestId = tempFormRequest.BulkFormRequestId
                    }, transaction);

                    var workflowInstance = await _workflowService.StartWorkflowAsync(
                        tempFormRequestId, 
                        formDefinition.WorkflowDefinitionId.Value, 
                        connection, 
                        (SqlTransaction)transaction);

                    // Update bulk request with workflow instance ID
                    var updateBulkSql = @"
                        UPDATE BulkFormRequests 
                        SET WorkflowInstanceId = @WorkflowInstanceId 
                        WHERE Id = @BulkRequestId";

                    await connection.ExecuteAsync(updateBulkSql, new
                    {
                        WorkflowInstanceId = workflowInstance.Id,
                        BulkRequestId = bulkRequestId
                    }, transaction);

                    // Update the temp FormRequest with workflow instance ID
                    var updateTempFormRequestSql = @"
                        UPDATE FormRequests 
                        SET WorkflowInstanceId = @WorkflowInstanceId 
                        WHERE Id = @FormRequestId";

                    await connection.ExecuteAsync(updateTempFormRequestSql, new
                    {
                        WorkflowInstanceId = workflowInstance.Id,
                        FormRequestId = tempFormRequestId
                    }, transaction);

                    bulkRequest.WorkflowInstanceId = workflowInstance.Id;

                    _logger.LogInformation("Started workflow instance {WorkflowInstanceId} for bulk request {BulkRequestId} with temp FormRequest {TempFormRequestId}", 
                        workflowInstance.Id, bulkRequestId, tempFormRequestId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start workflow for bulk request {BulkRequestId}", bulkRequestId);
                    // Continue without workflow - fallback to role-based approval
                }
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

                // Load associated bulk request items (new approach)
                var itemsSql = @"
                    SELECT 
                        bfri.Id,
                        bfri.BulkFormRequestId,
                        bfri.FieldValues,
                        bfri.OriginalValues,
                        bfri.RowNumber,
                        bfri.Status,
                        bfri.ValidationErrors,
                        bfri.ProcessingResult
                    FROM BulkFormRequestItems bfri
                    WHERE bfri.BulkFormRequestId = @BulkFormRequestId
                    ORDER BY bfri.RowNumber";

                var itemResults = await connection.QueryAsync(itemsSql, new { BulkFormRequestId = id });
                
                var items = new List<BulkFormRequestItem>();
                
                // Map and deserialize each item
                foreach (var row in itemResults)
                {
                    var item = new BulkFormRequestItem
                    {
                        Id = row.Id,
                        BulkFormRequestId = row.BulkFormRequestId,
                        RowNumber = row.RowNumber,
                        Status = (RequestStatus)row.Status,
                        ValidationErrors = row.ValidationErrors,
                        ProcessingResult = row.ProcessingResult
                    };
                    
                    // Deserialize JSON strings to dictionaries
                    if (row.FieldValues != null)
                    {
                        _logger.LogInformation($"Deserializing FieldValues: {row.FieldValues}");
                        item.FieldValues = JsonSerializer.Deserialize<Dictionary<string, object>>(row.FieldValues.ToString()) ?? new Dictionary<string, object>();
                    }
                    else
                    {
                        item.FieldValues = new Dictionary<string, object>();
                    }
                    
                    if (row.OriginalValues != null)
                    {
                        _logger.LogInformation($"Deserializing OriginalValues: {row.OriginalValues}");
                        item.OriginalValues = JsonSerializer.Deserialize<Dictionary<string, object>>(row.OriginalValues.ToString()) ?? new Dictionary<string, object>();
                    }
                    else
                    {
                        item.OriginalValues = new Dictionary<string, object>();
                    }
                    
                    items.Add(item);
                }
                
                bulkRequest.Items = items.ToList();

                // If this bulk request has a workflow, find the associated FormRequest for workflow progress
                if (bulkRequest.WorkflowInstanceId.HasValue)
                {
                    var workflowFormRequestSql = @"
                        SELECT Id FROM FormRequests 
                        WHERE BulkFormRequestId = @BulkFormRequestId 
                        AND WorkflowInstanceId = @WorkflowInstanceId";
                    
                    var workflowFormRequestId = await connection.QueryFirstOrDefaultAsync<int?>(
                        workflowFormRequestSql, 
                        new { 
                            BulkFormRequestId = id, 
                            WorkflowInstanceId = bulkRequest.WorkflowInstanceId 
                        });
                    
                    bulkRequest.WorkflowFormRequestId = workflowFormRequestId;
                }
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
            
            // Load Items count for display
            var itemsCount = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM BulkFormRequestItems WHERE BulkFormRequestId = @Id", new { Id = result.Id });
            
            // Note: We're not fully loading Items here for performance, just getting the count
            // The Items will be empty, but we know the count for display purposes
            result.Items = new List<BulkFormRequestItem>();
            result.SelectedRows = itemsCount; // Use SelectedRows to track actual count for display
            
            // If this bulk request has a workflow, find the associated FormRequest for workflow progress
            if (result.WorkflowInstanceId.HasValue)
            {
                var workflowFormRequestSql = @"
                    SELECT Id FROM FormRequests 
                    WHERE BulkFormRequestId = @BulkFormRequestId 
                    AND WorkflowInstanceId = @WorkflowInstanceId";
                
                var workflowFormRequestId = await connection.QueryFirstOrDefaultAsync<int?>(
                    workflowFormRequestSql, 
                    new { 
                        BulkFormRequestId = result.Id, 
                        WorkflowInstanceId = result.WorkflowInstanceId 
                    });
                
                result.WorkflowFormRequestId = workflowFormRequestId;
            }
        }
        
        return results.ToList();
    }

    public async Task<List<BulkFormRequest>> GetBulkFormRequestsForApprovalAsync(string userId, List<string> userRoles)
    {
        using var connection = new SqlConnection(_connectionString);
        
        var rolesList = string.Join(",", userRoles.Select(r => $"'{r}'"));
        
        // Get both role-based and workflow-based bulk requests that need approval
        var sql = @"
            SELECT DISTINCT bfr.*
            FROM BulkFormRequests bfr
            INNER JOIN FormDefinitions fd ON bfr.FormDefinitionId = fd.Id
            LEFT JOIN WorkflowInstances wi ON bfr.WorkflowInstanceId = wi.Id
            LEFT JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId 
                AND wsi.Status = 0 -- Pending status
            LEFT JOIN WorkflowSteps ws ON wsi.StepId = ws.Id
            WHERE bfr.Status = 0 AND bfr.RequestedBy != @UserId
            AND (
                -- Role-based approval (legacy)
                (bfr.WorkflowInstanceId IS NULL AND EXISTS (
                    SELECT 1 FROM STRING_SPLIT(fd.ApproverRoles, ',') ar
                    WHERE ar.value IN (" + rolesList + @")
                ))
                OR
                -- Workflow-based approval (new)
                (bfr.WorkflowInstanceId IS NOT NULL AND (
                    -- User assigned to current step
                    ws.AssignedUserId = @UserId
                    OR
                    -- User's role matches step role requirements
                    EXISTS (
                        SELECT 1 FROM STRING_SPLIT(ws.RequiredRoles, ',') sr
                        WHERE sr.value IN (" + rolesList + @")
                    )
                ))
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
            
            // Load Items count for display
            var itemsCount = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM BulkFormRequestItems WHERE BulkFormRequestId = @Id", new { Id = result.Id });
            
            result.Items = new List<BulkFormRequestItem>();
            result.SelectedRows = itemsCount; // Use SelectedRows to track actual count for display
            
            // If this bulk request has a workflow, find the associated FormRequest for workflow progress
            if (result.WorkflowInstanceId.HasValue)
            {
                var workflowFormRequestSql = @"
                    SELECT Id FROM FormRequests 
                    WHERE BulkFormRequestId = @BulkFormRequestId 
                    AND WorkflowInstanceId = @WorkflowInstanceId";
                
                var workflowFormRequestId = await connection.QueryFirstOrDefaultAsync<int?>(
                    workflowFormRequestSql, 
                    new { 
                        BulkFormRequestId = result.Id, 
                        WorkflowInstanceId = result.WorkflowInstanceId 
                    });
                
                result.WorkflowFormRequestId = workflowFormRequestId;
            }
        }
        
        return results.ToList();
    }

    public async Task<bool> ApproveBulkFormRequestAsync(int id, string userId, string userName, string? comments = null)
    {
        using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            using var transaction = await connection.BeginTransactionAsync();
            
            try
            {
                _logger.LogInformation("Approving bulk form request ID: {Id}", id);
                
                // Get the bulk request to check if it has a workflow
                var bulkRequest = await connection.QueryFirstOrDefaultAsync(
                    "SELECT WorkflowInstanceId, Status FROM BulkFormRequests WHERE Id = @Id", 
                    new { Id = id }, transaction);
                
                if (bulkRequest == null)
                {
                    _logger.LogWarning("Bulk request with ID {Id} not found", id);
                    await transaction.RollbackAsync();
                    return false;
                }
                
                if ((int)bulkRequest.Status != (int)RequestStatus.Pending)
                {
                    _logger.LogWarning("Bulk request with ID {Id} is not in pending state (Status: {Status})", id, (int)bulkRequest.Status);
                    await transaction.RollbackAsync();
                    return false;
                }
                
                // If this has a workflow, we should not directly approve it - the workflow should handle this
                if (bulkRequest.WorkflowInstanceId != null)
                {
                    _logger.LogWarning("Bulk request with ID {Id} has a workflow instance {WorkflowInstanceId}. Use workflow approval instead.", 
                        id, (int)bulkRequest.WorkflowInstanceId);
                    await transaction.RollbackAsync();
                    return false;
                }
                
                // Update the bulk request status to approved (legacy path)
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
                    _logger.LogWarning("No bulk request with ID {Id} was updated", id);
                    await transaction.RollbackAsync();
                    return false;
                }
                
                // Update all bulk request items to approved status
                var updateItemsSql = @"
                    UPDATE BulkFormRequestItems 
                    SET Status = 1 
                    WHERE BulkFormRequestId = @BulkFormRequestId AND Status = 0";

                await connection.ExecuteAsync(updateItemsSql, new { BulkFormRequestId = id }, transaction);
                
                // Commit the transaction for status updates
                await transaction.CommitAsync();
                _logger.LogInformation("Successfully approved bulk request {Id}", id);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving bulk request {Id}", id);
                await transaction.RollbackAsync();
                return false;
            }
        }
    }

    public async Task<bool> RejectBulkFormRequestAsync(int id, string userId, string userName, string rejectionReason)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = await connection.BeginTransactionAsync();
        
        try
        {
            // Get the bulk request to check if it has a workflow
            var bulkRequest = await connection.QueryFirstOrDefaultAsync(
                "SELECT WorkflowInstanceId, Status FROM BulkFormRequests WHERE Id = @Id", 
                new { Id = id }, transaction);
            
            if (bulkRequest == null)
            {
                _logger.LogWarning("Bulk request with ID {Id} not found", id);
                await transaction.RollbackAsync();
                return false;
            }
            
            if ((int)bulkRequest.Status != (int)RequestStatus.Pending)
            {
                _logger.LogWarning("Bulk request with ID {Id} is not in pending state (Status: {Status})", id, (int)bulkRequest.Status);
                await transaction.RollbackAsync();
                return false;
            }
            
            // If this has a workflow, we should not directly reject it - the workflow should handle this
            if (bulkRequest.WorkflowInstanceId != null)
            {
                _logger.LogWarning("Bulk request with ID {Id} has a workflow instance {WorkflowInstanceId}. Use workflow rejection instead.", 
                    id, (int)bulkRequest.WorkflowInstanceId);
                await transaction.RollbackAsync();
                return false;
            }
            
            var sql = @"
                UPDATE BulkFormRequests 
                SET Status = 2, ApprovedBy = @UserId, ApprovedByName = @UserName, ApprovedAt = @ApprovedAt,
                    RejectionReason = @RejectionReason, UpdatedBy = @UserId, UpdatedAt = @ApprovedAt
                WHERE Id = @Id AND Status = 0";

            var approvedAt = DateTime.UtcNow;
            var result = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId, UserName = userName, ApprovedAt = approvedAt, RejectionReason = rejectionReason }, transaction);

            if (result > 0)
            {
                // Also reject all bulk request items
                var itemsSql = @"
                    UPDATE BulkFormRequestItems 
                    SET Status = 2
                    WHERE BulkFormRequestId = @BulkFormRequestId AND Status = 0";

                await connection.ExecuteAsync(itemsSql, new { BulkFormRequestId = id }, transaction);
            }

            await transaction.CommitAsync();
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting bulk request {Id}", id);
            await transaction.RollbackAsync();
            return false;
        }
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
                
                // Load Items count for display
                var itemsCount = await connection.QuerySingleOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM BulkFormRequestItems WHERE BulkFormRequestId = @Id", new { Id = result.Id });
                
                // Note: We're not fully loading Items here for performance, just getting the count
                result.Items = new List<BulkFormRequestItem>();
                result.SelectedRows = itemsCount; // Use SelectedRows to track actual count for display
                
                // If this bulk request has a workflow, find the associated FormRequest for workflow progress
                if (result.WorkflowInstanceId.HasValue)
                {
                    var workflowFormRequestSql = @"
                        SELECT Id FROM FormRequests 
                        WHERE BulkFormRequestId = @BulkFormRequestId 
                        AND WorkflowInstanceId = @WorkflowInstanceId";
                    
                    var workflowFormRequestId = await connection.QueryFirstOrDefaultAsync<int?>(
                        workflowFormRequestSql, 
                        new { 
                            BulkFormRequestId = result.Id, 
                            WorkflowInstanceId = result.WorkflowInstanceId 
                        });
                    
                    result.WorkflowFormRequestId = workflowFormRequestId;
                }
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

    public async Task<List<BulkFormRequest>> GetAllBulkFormRequestsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        
        var sql = @"
            SELECT bfr.*
            FROM BulkFormRequests bfr
            ORDER BY bfr.RequestedAt DESC";

        var results = await connection.QueryAsync<BulkFormRequest>(sql);
        
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
            
            // Load Items count for display
            var itemsCount = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM BulkFormRequestItems WHERE BulkFormRequestId = @Id", new { Id = result.Id });
            
            // Note: We're not fully loading Items here for performance, just getting the count
            // The Items will be empty, but we know the count for display purposes
            result.Items = new List<BulkFormRequestItem>();
            result.SelectedRows = itemsCount; // Use SelectedRows to track actual count for display
            
            // If this bulk request has a workflow, find the associated FormRequest for workflow progress
            if (result.WorkflowInstanceId.HasValue)
            {
                var workflowFormRequestSql = @"
                    SELECT Id FROM FormRequests 
                    WHERE BulkFormRequestId = @BulkFormRequestId 
                    AND WorkflowInstanceId = @WorkflowInstanceId";
                
                var workflowFormRequestId = await connection.QueryFirstOrDefaultAsync<int?>(
                    workflowFormRequestSql, 
                    new { 
                        BulkFormRequestId = result.Id, 
                        WorkflowInstanceId = result.WorkflowInstanceId 
                    });
                
                result.WorkflowFormRequestId = workflowFormRequestId;
            }
        }
        
        return results.ToList();
    }
}
