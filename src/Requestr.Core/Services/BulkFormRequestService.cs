using Dapper;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using Requestr.Core.Models.DTOs;
using Requestr.Core.Services.Workflow;
using Requestr.Core.Utilities;
using System.Text.Json;

namespace Requestr.Core.Services;

public class BulkFormRequestService : IBulkFormRequestService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BulkFormRequestService> _logger;
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly IWorkflowInstanceService _workflowInstanceService;
    private readonly IWorkflowExecutionService _workflowExecutionService;
    private readonly IInputValidationService _inputValidationService;
    private readonly IDataService _dataService;
    private readonly string _connectionString;

    public BulkFormRequestService(
        IConfiguration configuration,
        ILogger<BulkFormRequestService> logger,
        IFormDefinitionService formDefinitionService,
        IWorkflowInstanceService workflowInstanceService,
        IWorkflowExecutionService workflowExecutionService,
        IInputValidationService inputValidationService,
        IDataService dataService)
    {
        _configuration = configuration;
        _logger = logger;
        _formDefinitionService = formDefinitionService;
        _workflowInstanceService = workflowInstanceService;
        _workflowExecutionService = workflowExecutionService;
        _inputValidationService = inputValidationService;
        _dataService = dataService;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
    }

    public async Task<SpreadsheetUploadResult> ProcessSpreadsheetUploadAsync(int formDefinitionId, Stream fileStream, string fileName)
    {
        var result = new SpreadsheetUploadResult();
        
        try
        {
            // Validate file extension
            if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add("Only .xlsx files are supported. Please download the template and use that format.");
                return result;
            }

            // Ensure we have a seekable working stream
            MemoryStream workingStream;
            if (!fileStream.CanSeek)
            {
                workingStream = new MemoryStream();
                await fileStream.CopyToAsync(workingStream);
                workingStream.Position = 0;
            }
            else
            {
                workingStream = new MemoryStream();
                fileStream.Position = 0;
                await fileStream.CopyToAsync(workingStream);
                workingStream.Position = 0;
            }

            // Validate file size
            if (workingStream.Length > 10 * 1024 * 1024)
            {
                result.Errors.Add("File size exceeds 10MB limit");
                return result;
            }

            // Get form definition to validate against
            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formDefinitionId);
            if (formDefinition == null)
            {
                result.Errors.Add("Form definition not found");
                return result;
            }

            // Read Excel file
            using var spreadsheet = SpreadsheetDocument.Open(workingStream, false);
            var workbookPart = spreadsheet.WorkbookPart;
            if (workbookPart == null)
            {
                result.Errors.Add("Invalid Excel file: no workbook found");
                return result;
            }

            var worksheetPart = workbookPart.WorksheetParts.FirstOrDefault();
            if (worksheetPart == null)
            {
                result.Errors.Add("Invalid Excel file: no worksheet found");
                return result;
            }

            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
            if (sheetData == null)
            {
                result.Errors.Add("Invalid Excel file: no data found");
                return result;
            }

            var rows = sheetData.Elements<Row>().ToList();
            if (rows.Count == 0)
            {
                result.Errors.Add("Excel file is empty");
                return result;
            }

            // Get shared strings table for resolving string values
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
                .Elements<SharedStringItem>()
                .Select(s => s.InnerText)
                .ToList() ?? new List<string>();

            // Read headers from first row
            var headerRow = rows[0];
            var headers = new List<string>();
            foreach (var cell in headerRow.Elements<Cell>())
            {
                var headerValue = GetCellValue(cell, sharedStrings);
                if (!string.IsNullOrWhiteSpace(headerValue))
                {
                    headers.Add(headerValue);
                }
            }

            if (!headers.Any())
            {
                result.Errors.Add("Excel file must contain column headers in the first row");
                return result;
            }

            // Validate headers against form fields
            var validationErrors = ValidateHeaders(headers.ToArray(), formDefinition.Fields);
            if (validationErrors.Any())
            {
                result.Errors.AddRange(validationErrors);
                return result;
            }

            // Build column index to header mapping
            var columnToHeader = new Dictionary<int, string>();
            var headerCells = headerRow.Elements<Cell>().ToList();
            for (int i = 0; i < headerCells.Count; i++)
            {
                var colIndex = GetColumnIndex(headerCells[i].CellReference?.Value);
                var headerValue = GetCellValue(headerCells[i], sharedStrings);
                if (!string.IsNullOrWhiteSpace(headerValue))
                {
                    columnToHeader[colIndex] = headerValue;
                }
            }

            // Process data rows (skip header)
            for (int rowIdx = 1; rowIdx < rows.Count; rowIdx++)
            {
                var row = rows[rowIdx];
                var rowNumber = rowIdx + 1; // 1-based for user display

                var rowValidation = await ValidateExcelRowAsync(row, columnToHeader, sharedStrings, formDefinition, rowNumber);
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

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Excel file");
            result.Errors.Add($"Error processing Excel file: {ex.Message}");
            return result;
        }
    }

    private static string GetCellValue(Cell cell, List<string> sharedStrings)
    {
        if (cell.CellValue == null)
            return string.Empty;

        var value = cell.CellValue.InnerText;

        // If it's a shared string, look it up
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (int.TryParse(value, out var index) && index < sharedStrings.Count)
            {
                return sharedStrings[index];
            }
        }

        // If it's a boolean
        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return value == "1" ? "TRUE" : "FALSE";
        }

        return value;
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
            return 0;

        var columnPart = new string(cellReference.TakeWhile(char.IsLetter).ToArray());
        int columnIndex = 0;
        foreach (var c in columnPart.ToUpperInvariant())
        {
            columnIndex = columnIndex * 26 + (c - 'A' + 1);
        }
        return columnIndex - 1; // 0-based
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

    private async Task<SpreadsheetRowValidationResult> ValidateExcelRowAsync(
        Row row, 
        Dictionary<int, string> columnToHeader, 
        List<string> sharedStrings,
        FormDefinition formDefinition, 
        int rowNumber)
    {
        var result = new SpreadsheetRowValidationResult
        {
            RowNumber = rowNumber,
            IsValid = true
        };

        try
        {
            // Build a dictionary of column name -> cell value for this row
            var rowData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in row.Elements<Cell>())
            {
                var colIndex = GetColumnIndex(cell.CellReference?.Value);
                if (columnToHeader.TryGetValue(colIndex, out var headerName))
                {
                    rowData[headerName] = GetCellValue(cell, sharedStrings);
                }
            }

            // Skip completely empty rows
            if (rowData.Values.All(string.IsNullOrWhiteSpace))
            {
                result.IsValid = false;
                result.Errors.Add("Empty row");
                return result;
            }

            foreach (var field in formDefinition.Fields.Where(f => f.IsVisible))
            {
                rowData.TryGetValue(field.Name, out var cellValue);
                
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

    private async Task<object?> ConvertAndValidateFieldValueAsync(string? cellValue, FormField field, SpreadsheetRowValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(cellValue))
        {
            return null;
        }

        try
        {
            // Use the validation service for security validation
            var (isValid, sanitizedValue, errors) = await _inputValidationService.ValidateAndSanitizeFieldAsync(cellValue, field);
            
            if (!isValid)
            {
                result.Errors.AddRange(errors);
                result.IsValid = false;
                return null;
            }

            // Convert the sanitized value to appropriate type based on SQL data type
            object? convertedValue = SqlTypeConverter.ConvertToSqlType(sanitizedValue, field.SqlDataType);
            
            return convertedValue;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Invalid {field.DataType} value in column '{field.Name}': {cellValue} - {ex.Message}");
            result.IsValid = false;
            return null;
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
                                            RequestedBy, RequestedAt, Comments, CreatedBy, CreatedAt)
                OUTPUT INSERTED.Id
                VALUES (@FormDefinitionId, @RequestType, @FileName, @TotalRows, @ValidRows, @InvalidRows, @SelectedRows,
                        @RequestedBy, @RequestedAt, @Comments, @CreatedBy, @CreatedAt)";

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
                        FieldValues = new Dictionary<string, object?> 
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
                                                RequestedBy, RequestedAt, Comments, BulkFormRequestId)
                        OUTPUT INSERTED.Id
                        VALUES (@FormDefinitionId, @RequestType, @FieldValues, @OriginalValues, @Status,
                                @RequestedBy, @RequestedAt, @Comments, @BulkFormRequestId)";

                    var tempFormRequestId = await connection.QuerySingleAsync<int>(tempFormRequestSql, new
                    {
                        FormDefinitionId = tempFormRequest.FormDefinitionId,
                        RequestType = (int)tempFormRequest.RequestType,
                        FieldValues = JsonSerializer.Serialize(tempFormRequest.FieldValues),
                        OriginalValues = JsonSerializer.Serialize(tempFormRequest.OriginalValues ?? new Dictionary<string, object?>()),
                        Status = (int)tempFormRequest.Status,
                        RequestedBy = tempFormRequest.RequestedBy,
                        RequestedAt = tempFormRequest.RequestedAt,
                        Comments = tempFormRequest.Comments,
                        BulkFormRequestId = tempFormRequest.BulkFormRequestId
                    }, transaction);

                    var workflowInstanceId = await _workflowInstanceService.StartWorkflowAsync(
                        connection,
                        transaction,
                        tempFormRequestId, 
                        formDefinition.WorkflowDefinitionId.Value,
                        userId);

                    // Update bulk request with workflow instance ID
                    var updateBulkSql = @"
                        UPDATE BulkFormRequests 
                        SET WorkflowInstanceId = @WorkflowInstanceId 
                        WHERE Id = @BulkRequestId";

                    await connection.ExecuteAsync(updateBulkSql, new
                    {
                        WorkflowInstanceId = workflowInstanceId,
                        BulkRequestId = bulkRequestId
                    }, transaction);

                    // Update the temp FormRequest with workflow instance ID
                    var updateTempFormRequestSql = @"
                        UPDATE FormRequests 
                        SET WorkflowInstanceId = @WorkflowInstanceId 
                        WHERE Id = @FormRequestId";

                    await connection.ExecuteAsync(updateTempFormRequestSql, new
                    {
                        WorkflowInstanceId = workflowInstanceId,
                        FormRequestId = tempFormRequestId
                    }, transaction);

                    bulkRequest.WorkflowInstanceId = workflowInstanceId;

                    _logger.LogInformation("Started workflow instance {WorkflowInstanceId} for bulk request {BulkRequestId} with temp FormRequest {TempFormRequestId}", 
                        workflowInstanceId, bulkRequestId, tempFormRequestId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start workflow for bulk request {BulkRequestId}", bulkRequestId);
                    // Continue without workflow - fallback to role-based approval
                }
            }

            // If no workflow, auto-approve the bulk request (matching single form request behavior)
            if (!formDefinition.WorkflowDefinitionId.HasValue)
            {
                _logger.LogInformation("No workflow for form definition {FormDefinitionId}, auto-approving bulk request {BulkRequestId}",
                    formDefinition.Id, bulkRequestId);

                var autoApproveAt = DateTime.UtcNow;
                var autoApproveSql = @"
                    UPDATE BulkFormRequests 
                    SET Status = @Status, ApprovedBy = @UserId, ApprovedAt = @ApprovedAt,
                        UpdatedBy = @UserId, UpdatedAt = @ApprovedAt
                    WHERE Id = @Id";

                await connection.ExecuteAsync(autoApproveSql, new
                {
                    Id = bulkRequestId,
                    Status = (int)RequestStatus.Approved,
                    UserId = userId,
                    ApprovedAt = autoApproveAt
                }, transaction);

                bulkRequest.Status = RequestStatus.Approved;

                // Set all items to Approved
                var approveItemsSql = @"
                    UPDATE BulkFormRequestItems 
                    SET Status = @Status 
                    WHERE BulkFormRequestId = @BulkFormRequestId AND Status = @PendingStatus";

                await connection.ExecuteAsync(approveItemsSql, new
                {
                    BulkFormRequestId = bulkRequestId,
                    Status = (int)RequestStatus.Approved,
                    PendingStatus = (int)RequestStatus.Pending
                }, transaction);
            }

            // Record history entry
            await RecordBulkHistoryAsync(connection, transaction, bulkRequestId,
                FormRequestChangeType.Created, userId, userName, bulkRequest.Comments,
                $"{bulkRequest.SelectedRows} items, {bulkRequest.RequestType} request");

            if (!formDefinition.WorkflowDefinitionId.HasValue)
            {
                await RecordBulkHistoryAsync(connection, transaction, bulkRequestId,
                    FormRequestChangeType.Approved, userId, userName, "Auto-approved (no workflow)");
            }

            await transaction.CommitAsync();

            // Post-commit: process pending webhook step or apply data for no-workflow requests
            if (bulkRequest.WorkflowInstanceId.HasValue)
            {
                try
                {
                    await _workflowExecutionService.ProcessPendingWebhookStepAsync(bulkRequest.WorkflowInstanceId.Value);
                }
                catch (Exception webhookEx)
                {
                    _logger.LogError(webhookEx, "Error processing webhook step for bulk workflow {WorkflowId}", bulkRequest.WorkflowInstanceId.Value);
                }
            }
            else
            {
                // No workflow — apply data changes immediately
                try
                {
                    await ApplyBulkRequestDataAsync(bulkRequestId, formDefinition);
                }
                catch (Exception applyEx)
                {
                    _logger.LogError(applyEx, "Error auto-applying bulk request {BulkRequestId}", bulkRequestId);
                }
            }

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
                SELECT bfr.*, COALESCE(uReq.DisplayName, bfr.RequestedBy) AS RequestedByName, COALESCE(uApp.DisplayName, bfr.ApprovedBy) AS ApprovedByName
                FROM BulkFormRequests bfr
                LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, bfr.RequestedBy) = uReq.UserObjectId
                LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, bfr.ApprovedBy) = uApp.UserObjectId
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
                        item.FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.FieldValues.ToString()) ?? new Dictionary<string, object?>();
                    }
                    else
                    {
                        item.FieldValues = new Dictionary<string, object?>();
                    }
                    
                    if (row.OriginalValues != null)
                    {
                        _logger.LogInformation($"Deserializing OriginalValues: {row.OriginalValues}");
                        item.OriginalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(row.OriginalValues.ToString()) ?? new Dictionary<string, object?>();
                    }
                    else
                    {
                        item.OriginalValues = new Dictionary<string, object?>();
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

                // Load history
                var historySql = @"
                    SELECT bfh.*, COALESCE(u.DisplayName, bfh.ChangedBy) AS ChangedByName
                    FROM BulkFormRequestHistory bfh
                    LEFT JOIN Users u ON TRY_CONVERT(uniqueidentifier, bfh.ChangedBy) = u.UserObjectId
                    WHERE bfh.BulkFormRequestId = @BulkFormRequestId
                    ORDER BY bfh.ChangedAt";
                
                bulkRequest.History = (await connection.QueryAsync<BulkFormRequestHistory>(
                    historySql, new { BulkFormRequestId = id })).ToList();
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
            SELECT bfr.*, COALESCE(uReq.DisplayName, bfr.RequestedBy) AS RequestedByName, COALESCE(uApp.DisplayName, bfr.ApprovedBy) AS ApprovedByName
            FROM BulkFormRequests bfr
            LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, bfr.RequestedBy) = uReq.UserObjectId
            LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, bfr.ApprovedBy) = uApp.UserObjectId
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

    public async Task<List<BulkFormRequest>> GetAccessibleBulkFormRequestsAsync(string userId, List<string> userRoles)
    {
        var isAdmin = userRoles.Contains("Admin");
        
        if (isAdmin)
        {
            // Admin can see all bulk requests
            return await GetAllBulkFormRequestsAsync();
        }
        
        // For non-admin users, combine:
        // 1. Bulk requests they created
        // 2. Bulk requests they can approve (pending workflow step assigned to their role)
        // 3. Bulk requests where their roles are assigned to any workflow approval step
        var ownRequests = await GetBulkFormRequestsByUserAsync(userId);
        var forApproval = await GetBulkFormRequestsForApprovalAsync(userId, userRoles);
        var participated = await GetBulkFormRequestsUserParticipatedInAsync(userId, userRoles);
        
        // Combine and deduplicate by Id
        var combined = ownRequests
            .Concat(forApproval)
            .Concat(participated)
            .GroupBy(r => r.Id)
            .Select(g => g.First())
            .OrderByDescending(r => r.RequestedAt)
            .ToList();
        
        return combined;
    }

    public async Task<List<BulkFormRequest>> GetBulkFormRequestsUserParticipatedInAsync(string userId, List<string> userRoles)
    {
        using var connection = new SqlConnection(_connectionString);
        
        var userRolesJson = System.Text.Json.JsonSerializer.Serialize(userRoles);
        
        // Get bulk requests where user's roles are assigned to any approval step in the workflow
        var sql = @"
            SELECT DISTINCT bfr.*, COALESCE(uReq.DisplayName, bfr.RequestedBy) AS RequestedByName, COALESCE(uApp.DisplayName, bfr.ApprovedBy) AS ApprovedByName
            FROM BulkFormRequests bfr
            INNER JOIN WorkflowInstances wi ON bfr.WorkflowInstanceId = wi.Id
            LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, bfr.RequestedBy) = uReq.UserObjectId
            LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, bfr.ApprovedBy) = uApp.UserObjectId
            WHERE bfr.RequestedBy != @UserId
            AND EXISTS (
                SELECT 1 FROM WorkflowSteps ws
                WHERE ws.WorkflowDefinitionId = wi.WorkflowDefinitionId
                AND ws.StepType = @ApprovalStepType
                AND ws.AssignedRoles IS NOT NULL AND ws.AssignedRoles != '[]'
                AND EXISTS (
                    SELECT 1 FROM OPENJSON(ws.AssignedRoles) sr
                    WHERE sr.value IN (SELECT value FROM OPENJSON(@UserRolesJson))
                )
            )
            ORDER BY bfr.RequestedAt DESC";

        var results = await connection.QueryAsync<BulkFormRequest>(sql, new { 
            UserId = userId, 
            UserRolesJson = userRolesJson,
            ApprovalStepType = (int)WorkflowStepType.Approval
        });
        
        // Load FormDefinition for each result
        foreach (var result in results)
        {
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
            result.SelectedRows = itemsCount;
            
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
        
        var userRolesJson = System.Text.Json.JsonSerializer.Serialize(userRoles);
        
        // Get workflow-based bulk requests that need approval (pending step assigned to user's role)
        var sql = @"
            SELECT DISTINCT bfr.*, COALESCE(uReq.DisplayName, bfr.RequestedBy) AS RequestedByName, COALESCE(uApp.DisplayName, bfr.ApprovedBy) AS ApprovedByName
            FROM BulkFormRequests bfr
            INNER JOIN WorkflowInstances wi ON bfr.WorkflowInstanceId = wi.Id
            INNER JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId AND wsi.Status = 0 -- Pending status
            INNER JOIN WorkflowSteps ws ON wsi.StepId = ws.StepId AND wi.WorkflowDefinitionId = ws.WorkflowDefinitionId
            LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, bfr.RequestedBy) = uReq.UserObjectId
            LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, bfr.ApprovedBy) = uApp.UserObjectId
            WHERE bfr.Status = 0 AND bfr.RequestedBy != @UserId
            AND ws.StepType = @ApprovalStepType
            AND ws.AssignedRoles IS NOT NULL AND ws.AssignedRoles != '[]' 
            AND EXISTS (
                SELECT 1 FROM OPENJSON(ws.AssignedRoles) sr
                WHERE sr.value IN (SELECT value FROM OPENJSON(@UserRolesJson))
            )
            ORDER BY bfr.RequestedAt";

        var results = await connection.QueryAsync<BulkFormRequest>(sql, new { 
            UserId = userId, 
            UserRolesJson = userRolesJson,
            ApprovalStepType = (int)WorkflowStepType.Approval
        });
        
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
        // Sanitize comments if provided
        var sanitizedComments = !string.IsNullOrEmpty(comments) 
            ? _inputValidationService.SanitizeComments(comments)
            : comments;
            
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
                    SET Status = 1, ApprovedBy = @UserId, ApprovedAt = @ApprovedAt,
                        Comments = CASE WHEN @Comments IS NOT NULL THEN @Comments ELSE Comments END,
                        UpdatedBy = @UserId, UpdatedAt = @ApprovedAt
                    WHERE Id = @Id AND Status = 0";

                var approvedAt = DateTime.UtcNow;
                var result = await connection.ExecuteAsync(sql, new { Id = id, UserId = userId, UserName = userName, ApprovedAt = approvedAt, Comments = sanitizedComments }, transaction);

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
                
                // Record history entry
                await RecordBulkHistoryAsync(connection, transaction, id,
                    FormRequestChangeType.Approved, userId, userName, sanitizedComments);

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
                SET Status = 2, ApprovedBy = @UserId, ApprovedAt = @ApprovedAt,
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

            // Record history entry
            if (result > 0)
            {
                await RecordBulkHistoryAsync(connection, transaction, id,
                    FormRequestChangeType.Rejected, userId, userName, rejectionReason);
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

    /// <summary>
    /// Applies data changes for a no-workflow bulk request after auto-approval.
    /// Called post-commit so items are already in Approved status.
    /// </summary>
    private async Task ApplyBulkRequestDataAsync(int bulkRequestId, FormDefinition formDefinition)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            // Get bulk request details
            var bulkRequest = await connection.QuerySingleOrDefaultAsync<BulkFormRequest>(
                "SELECT * FROM BulkFormRequests WHERE Id = @Id", new { Id = bulkRequestId });

            if (bulkRequest == null)
            {
                _logger.LogWarning("Bulk request {BulkRequestId} not found for auto-apply", bulkRequestId);
                return;
            }

            var requestType = bulkRequest.RequestType;
            var dbConnectionName = formDefinition.DatabaseConnectionName;
            var tableName = formDefinition.TableName;
            var schema = formDefinition.Schema;
            var fields = formDefinition.Fields;

            // Get all approved items
            var items = await connection.QueryAsync(
                @"SELECT Id, FieldValues, OriginalValues, RowNumber
                  FROM BulkFormRequestItems
                  WHERE BulkFormRequestId = @BulkRequestId AND Status = @ApprovedStatus
                  ORDER BY RowNumber",
                new { BulkRequestId = bulkRequestId, ApprovedStatus = (int)RequestStatus.Approved });

            if (!items.Any())
            {
                _logger.LogInformation("No approved items to apply for bulk request {BulkRequestId}", bulkRequestId);
                return;
            }

            _logger.LogInformation("Auto-applying {ItemCount} items for no-workflow bulk request {BulkRequestId}",
                items.Count(), bulkRequestId);

            int successCount = 0;
            int failureCount = 0;

            foreach (var item in items)
            {
                try
                {
                    var fieldValuesJson = item.FieldValues?.ToString() ?? "{}";
                    var originalValuesJson = item.OriginalValues?.ToString() ?? "{}";

                    var fieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldValuesJson) ?? new Dictionary<string, object?>();
                    var originalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(originalValuesJson) ?? new Dictionary<string, object?>();

                    fieldValues = SqlTypeConverter.ConvertDictionary(fieldValues, fields);
                    originalValues = SqlTypeConverter.ConvertDictionary(originalValues, fields);

                    bool itemSuccess = false;
                    string processingResult;

                    switch (requestType)
                    {
                        case RequestType.Insert:
                            itemSuccess = await _dataService.InsertDataAsync(dbConnectionName, tableName, schema, fieldValues);
                            processingResult = itemSuccess ? "Successfully inserted into database" : "Failed to insert into database";
                            break;

                        case RequestType.Update:
                            var pkColumns = await _dataService.GetPrimaryKeyColumnsAsync(dbConnectionName, tableName, schema);
                            if (pkColumns.Count == 0)
                                throw new InvalidOperationException($"No primary key found for table {schema}.{tableName}");

                            var whereConditions = new Dictionary<string, object?>();
                            foreach (var pk in pkColumns)
                            {
                                if (originalValues.ContainsKey(pk))
                                    whereConditions[pk] = originalValues[pk];
                                else
                                    throw new InvalidOperationException($"Primary key column '{pk}' not found in original values for item {(int)item.Id}");
                            }

                            itemSuccess = await _dataService.UpdateDataAsync(dbConnectionName, tableName, schema, fieldValues, whereConditions);
                            processingResult = itemSuccess ? "Successfully updated in database" : "Failed to update in database";
                            break;

                        case RequestType.Delete:
                            var deletePkColumns = await _dataService.GetPrimaryKeyColumnsAsync(dbConnectionName, tableName, schema);
                            if (deletePkColumns.Count == 0)
                                throw new InvalidOperationException($"No primary key found for table {schema}.{tableName}");

                            var deleteConditions = new Dictionary<string, object?>();
                            foreach (var pk in deletePkColumns)
                            {
                                if (originalValues.ContainsKey(pk))
                                    deleteConditions[pk] = originalValues[pk];
                                else
                                    throw new InvalidOperationException($"Primary key column '{pk}' not found in original values for item {(int)item.Id}");
                            }

                            itemSuccess = await _dataService.DeleteDataAsync(dbConnectionName, tableName, schema, deleteConditions);
                            processingResult = itemSuccess ? "Successfully deleted from database" : "Failed to delete from database";
                            break;

                        default:
                            processingResult = $"Unknown request type: {requestType}";
                            break;
                    }

                    await connection.ExecuteAsync(
                        @"UPDATE BulkFormRequestItems SET Status = @Status, ProcessingResult = @ProcessingResult WHERE Id = @ItemId",
                        new
                        {
                            ItemId = item.Id,
                            Status = itemSuccess ? (int)RequestStatus.Applied : (int)RequestStatus.Failed,
                            ProcessingResult = processingResult
                        });

                    if (itemSuccess) successCount++;
                    else failureCount++;
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logger.LogError(ex, "Error auto-applying bulk item {ItemId} (row {RowNumber})", (object)item.Id, (object)item.RowNumber);

                    await connection.ExecuteAsync(
                        @"UPDATE BulkFormRequestItems SET Status = @Status, ProcessingResult = @ProcessingResult WHERE Id = @ItemId",
                        new
                        {
                            ItemId = item.Id,
                            Status = (int)RequestStatus.Failed,
                            ProcessingResult = $"Error: {ex.Message}"
                        });
                }
            }

            // Update bulk request status
            var overallStatus = failureCount == 0 ? RequestStatus.Applied :
                                successCount > 0 ? RequestStatus.Applied : RequestStatus.Failed;
            var processingSummary = $"Processed {successCount + failureCount} items. {successCount} succeeded, {failureCount} failed.";

            await connection.ExecuteAsync(
                @"UPDATE BulkFormRequests SET Status = @Status, ProcessingSummary = @ProcessingSummary WHERE Id = @BulkRequestId",
                new
                {
                    BulkRequestId = bulkRequestId,
                    Status = (int)overallStatus,
                    ProcessingSummary = processingSummary
                });

            // Record history
            var historyChangeType = failureCount == 0 ? FormRequestChangeType.Applied : FormRequestChangeType.Failed;
            await connection.ExecuteAsync(
                @"INSERT INTO BulkFormRequestHistory (BulkFormRequestId, ChangeType, ChangedBy, ChangedByName, ChangedAt, Comments, Details)
                  VALUES (@BulkFormRequestId, @ChangeType, 'system', 'System', @ChangedAt, @Comments, NULL)",
                new
                {
                    BulkFormRequestId = bulkRequestId,
                    ChangeType = (int)historyChangeType,
                    ChangedAt = DateTime.UtcNow,
                    Comments = processingSummary
                });

            _logger.LogInformation("Auto-apply complete for bulk request {BulkRequestId}. {Summary}", bulkRequestId, processingSummary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during auto-apply of bulk request {BulkRequestId}", bulkRequestId);
        }
    }

    /// <summary>
    /// Records a history entry for a bulk form request within an existing transaction.
    /// </summary>
    private static async Task RecordBulkHistoryAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        int bulkFormRequestId,
        FormRequestChangeType changeType,
        string changedBy,
        string changedByName,
        string? comments = null,
        string? details = null)
    {
        const string sql = @"
            INSERT INTO BulkFormRequestHistory (BulkFormRequestId, ChangeType, ChangedBy, ChangedByName, ChangedAt, Comments, Details)
            VALUES (@BulkFormRequestId, @ChangeType, @ChangedBy, @ChangedByName, @ChangedAt, @Comments, @Details)";

        await connection.ExecuteAsync(sql, new
        {
            BulkFormRequestId = bulkFormRequestId,
            ChangeType = (int)changeType,
            ChangedBy = changedBy,
            ChangedByName = changedByName,
            ChangedAt = DateTime.UtcNow,
            Comments = comments,
            Details = details
        }, transaction);
    }

    public async Task<List<BulkFormRequest>> GetBulkFormRequestsByFormDefinitionIdAsync(int formDefinitionId, int limit = 10)
    {
        _logger.LogInformation($"Getting bulk form requests for form definition ID: {formDefinitionId}, limit: {limit}");
        
        try
        {
            using var connection = new SqlConnection(_connectionString);
            
            var sql = @"
                SELECT TOP (@Limit) bfr.*, COALESCE(uReq.DisplayName, bfr.RequestedBy) AS RequestedByName, COALESCE(uApp.DisplayName, bfr.ApprovedBy) AS ApprovedByName
                FROM BulkFormRequests bfr
                LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, bfr.RequestedBy) = uReq.UserObjectId
                LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, bfr.ApprovedBy) = uApp.UserObjectId
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
            SELECT bfr.*, COALESCE(uReq.DisplayName, bfr.RequestedBy) AS RequestedByName, COALESCE(uApp.DisplayName, bfr.ApprovedBy) AS ApprovedByName
            FROM BulkFormRequests bfr
            LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, bfr.RequestedBy) = uReq.UserObjectId
            LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, bfr.ApprovedBy) = uApp.UserObjectId
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
