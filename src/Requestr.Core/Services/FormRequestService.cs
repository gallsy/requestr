using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Requestr.Core.Interfaces;
using Requestr.Core.Models;
using System.Text.Json;

namespace Requestr.Core.Services;

public class FormRequestService : IFormRequestService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FormRequestService> _logger;
    private readonly IDataService _dataService;
    private readonly IFormDefinitionService _formDefinitionService;
    private readonly IWorkflowService _workflowService;
    private readonly string _connectionString;

    public FormRequestService(
        IConfiguration configuration, 
        ILogger<FormRequestService> logger,
        IDataService dataService,
        IFormDefinitionService formDefinitionService,
        IWorkflowService workflowService)
    {
        _configuration = configuration;
        _logger = logger;
        _dataService = dataService;
        _formDefinitionService = formDefinitionService;
        _workflowService = workflowService;
        _connectionString = _configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("DefaultConnection not found in configuration");
    }

    // Helper methods for enum conversion
    private static RequestType ParseRequestType(string requestType)
    {
        return requestType?.ToUpper() switch
        {
            "INSERT" => RequestType.Insert,
            "UPDATE" => RequestType.Update,
            "DELETE" => RequestType.Delete,
            _ => throw new ArgumentException($"Invalid RequestType: {requestType}")
        };
    }

    private static string RequestTypeToString(RequestType requestType)
    {
        return requestType switch
        {
            RequestType.Insert => "INSERT",
            RequestType.Update => "UPDATE",
            RequestType.Delete => "DELETE",
            _ => throw new ArgumentException($"Invalid RequestType: {requestType}")
        };
    }

    private static FormRequestChangeType ParseChangeType(string changeType)
    {
        return changeType?.ToUpper() switch
        {
            "CREATED" => FormRequestChangeType.Created,
            "UPDATED" => FormRequestChangeType.Updated,
            "STATUSCHANGED" => FormRequestChangeType.StatusChanged,
            "APPROVED" => FormRequestChangeType.Approved,
            "REJECTED" => FormRequestChangeType.Rejected,
            "APPLIED" => FormRequestChangeType.Applied,
            "FAILED" => FormRequestChangeType.Failed,
            _ => throw new ArgumentException($"Invalid ChangeType: {changeType}")
        };
    }

    private static string ChangeTypeToString(FormRequestChangeType changeType)
    {
        return changeType switch
        {
            FormRequestChangeType.Created => "Created",
            FormRequestChangeType.Updated => "Updated",
            FormRequestChangeType.StatusChanged => "StatusChanged",
            FormRequestChangeType.Approved => "Approved",
            FormRequestChangeType.Rejected => "Rejected",
            FormRequestChangeType.Applied => "Applied",
            FormRequestChangeType.Failed => "Failed",
            _ => throw new ArgumentException($"Invalid ChangeType: {changeType}")
        };
    }

    private FormRequest CreateFormRequestFromRow(dynamic row)
    {
        return new FormRequest
        {
            Id = (int)row.Id,
            FormDefinitionId = (int)row.FormDefinitionId,
            RequestType = ParseRequestType((string)row.RequestType),
            Status = (RequestStatus)(int)row.Status,
            RequestedBy = (string)row.RequestedBy,
            RequestedByName = (string)row.RequestedByName,
            RequestedAt = (DateTime)row.RequestedAt,
            ApprovedBy = (string?)row.ApprovedBy,
            ApprovedByName = (string?)row.ApprovedByName,
            ApprovedAt = (DateTime?)row.ApprovedAt,
            RejectionReason = (string?)row.RejectionReason,
            Comments = (string?)row.Comments,
            AppliedRecordKey = (string?)row.AppliedRecordKey,
            FailureMessage = (string?)row.FailureMessage,
            WorkflowInstanceId = (int?)row.WorkflowInstanceId,
            FormDefinition = new FormDefinition { Name = (string)row.FormName, Description = (string)row.FormDescription },
            FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.FieldValuesJson ?? "{}")) ?? new Dictionary<string, object?>(),
            OriginalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.OriginalValuesJson ?? "{}")) ?? new Dictionary<string, object?>()
        };
    }

    public async Task<List<FormRequest>> GetFormRequestsAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                       fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, fr.RequestedByName, 
                       fr.RequestedAt, fr.ApprovedBy, fr.ApprovedByName, fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                       fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId,
                       fd.Name as FormName, fd.Description as FormDescription
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                ORDER BY fr.RequestedAt DESC";

            var requests = await connection.QueryAsync(sql);
            var result = new List<FormRequest>();

            foreach (var row in requests)
            {
                var request = CreateFormRequestFromRow(row);
                result.Add(request);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests");
            throw;
        }
    }

    public async Task<List<FormRequest>> GetFormRequestsByUserAsync(string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                       fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, fr.RequestedByName, 
                       fr.RequestedAt, fr.ApprovedBy, fr.ApprovedByName, fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                       fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId,
                       fd.Name as FormName, fd.Description as FormDescription
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                WHERE fr.RequestedBy = @UserId
                ORDER BY fr.RequestedAt DESC";

            var requests = await connection.QueryAsync(sql, new { UserId = userId });
            var result = new List<FormRequest>();

            foreach (var row in requests)
            {
                var request = CreateFormRequestFromRow(row);
                result.Add(request);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<FormRequest>> GetFormRequestsForApprovalAsync(List<string> approverRoles)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // This is a simplified approach - in practice, you might want to create a more sophisticated role matching system
            var roleParams = string.Join(",", approverRoles.Select((_, i) => $"@role{i}"));
            var parameters = new DynamicParameters();
            for (int i = 0; i < approverRoles.Count; i++)
            {
                parameters.Add($"role{i}", approverRoles[i]);
            }

            var sql = $@"
                SELECT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                       fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, fr.RequestedByName, 
                       fr.RequestedAt, fr.ApprovedBy, fr.ApprovedByName, fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                       fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId,
                       fd.Name as FormName, fd.Description as FormDescription, fd.ApproverRoles
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                WHERE fr.Status = @Status
                ORDER BY fr.RequestedAt DESC";

            parameters.Add("Status", (int)RequestStatus.Pending);

            var requests = await connection.QueryAsync(sql, parameters);
            var result = new List<FormRequest>();

            foreach (var row in requests)
            {
                // Check if any of the user's roles match the form's approver roles
                var formApproverRoles = JsonSerializer.Deserialize<List<string>>((string)(row.ApproverRoles ?? "[]")) ?? new List<string>();
                if (formApproverRoles.Any(role => approverRoles.Contains(role)))
                {
                    var request = CreateFormRequestFromRow(row);
                    result.Add(request);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for approval with roles {Roles}", string.Join(", ", approverRoles));
            throw;
        }
    }

    public async Task<FormRequest?> GetFormRequestAsync(int id)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                       fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, fr.RequestedByName, 
                       fr.RequestedAt, fr.ApprovedBy, fr.ApprovedByName, fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                       fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId,
                       fd.Name as FormName, fd.Description as FormDescription
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                WHERE fr.Id = @Id";

            var row = await connection.QueryFirstOrDefaultAsync(sql, new { Id = id });
            
            if (row == null) return null;

            var request = CreateFormRequestFromRow(row);

            return request;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form request {Id}", id);
            throw;
        }
    }

    public async Task<FormRequest> CreateFormRequestAsync(FormRequest formRequest)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Check if the form has a workflow definition
            var workflowDefinition = await _workflowService.GetWorkflowDefinitionByFormAsync(formRequest.FormDefinitionId);
            
            var sql = @"
                INSERT INTO FormRequests (FormDefinitionId, RequestType, FieldValues, OriginalValues, Status, RequestedBy, RequestedByName, RequestedAt, Comments, AppliedRecordKey, FailureMessage, WorkflowInstanceId)
                OUTPUT INSERTED.Id
                VALUES (@FormDefinitionId, @RequestType, @FieldValues, @OriginalValues, @Status, @RequestedBy, @RequestedByName, @RequestedAt, @Comments, @AppliedRecordKey, @FailureMessage, @WorkflowInstanceId)";

            // Set initial status based on whether workflow exists
            formRequest.Status = workflowDefinition != null ? RequestStatus.Pending : RequestStatus.Approved;

            var id = await connection.QuerySingleAsync<int>(sql, new
            {
                formRequest.FormDefinitionId,
                RequestType = RequestTypeToString(formRequest.RequestType),
                FieldValues = JsonSerializer.Serialize(formRequest.FieldValues),
                OriginalValues = JsonSerializer.Serialize(formRequest.OriginalValues),
                Status = (int)formRequest.Status,
                formRequest.RequestedBy,
                formRequest.RequestedByName,
                formRequest.RequestedAt,
                formRequest.Comments,
                formRequest.AppliedRecordKey,
                formRequest.FailureMessage,
                WorkflowInstanceId = (int?)null // Will be updated after workflow creation
            }, transaction);

            formRequest.Id = id;

            // Start workflow if one exists
            if (workflowDefinition != null)
            {
                // Use the overload that accepts existing connection and transaction to avoid deadlocks
                var workflowInstance = await _workflowService.StartWorkflowAsync(id, workflowDefinition.Id, connection, transaction);
                formRequest.WorkflowInstanceId = workflowInstance.Id;

                // Update the form request with the workflow instance ID
                await connection.ExecuteAsync(
                    "UPDATE FormRequests SET WorkflowInstanceId = @WorkflowInstanceId WHERE Id = @Id",
                    new { WorkflowInstanceId = workflowInstance.Id, Id = id },
                    transaction, commandTimeout: 300);

                _logger.LogInformation("Started workflow {WorkflowId} for form request {RequestId}", workflowInstance.Id, id);
            }
            else
            {
                // No workflow - auto-approve and apply if needed
                if (formRequest.Status == RequestStatus.Approved)
                {
                    // Auto-apply approved requests without workflows
                    // Note: This would be done outside the transaction in a real implementation
                    _logger.LogInformation("Form request {RequestId} auto-approved (no workflow)", id);
                }
            }
            
            // Record the creation in history
            await RecordChangeWithTransactionAsync(
                connection,
                transaction,
                formRequest.Id,
                FormRequestChangeType.Created,
                null,
                new Dictionary<string, object?>
                {
                    { "RequestType", formRequest.RequestType },
                    { "FieldValues", formRequest.FieldValues },
                    { "OriginalValues", formRequest.OriginalValues },
                    { "Status", formRequest.Status.ToString() },
                    { "Comments", formRequest.Comments },
                    { "WorkflowInstanceId", formRequest.WorkflowInstanceId }
                },
                formRequest.RequestedBy,
                formRequest.RequestedByName,
                workflowDefinition != null ? "Request created and workflow started" : "Request created (no workflow)"
            );

            await transaction.CommitAsync();
            return formRequest;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error creating form request");
            throw;
        }
    }

    public async Task<FormRequest> UpdateFormRequestAsync(FormRequest formRequest)
    {
        try
        {
            // Get the current version for comparison
            var currentRequest = await GetFormRequestAsync(formRequest.Id);
            if (currentRequest == null)
            {
                throw new ArgumentException($"Form request with ID {formRequest.Id} not found");
            }

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                UPDATE FormRequests 
                SET RequestType = @RequestType, FieldValues = @FieldValues, OriginalValues = @OriginalValues, 
                    Status = @Status, Comments = @Comments, AppliedRecordKey = @AppliedRecordKey, FailureMessage = @FailureMessage
                WHERE Id = @Id";

            await connection.ExecuteAsync(sql, new
            {
                formRequest.Id,
                RequestType = RequestTypeToString(formRequest.RequestType),
                FieldValues = JsonSerializer.Serialize(formRequest.FieldValues),
                OriginalValues = JsonSerializer.Serialize(formRequest.OriginalValues),
                Status = (int)formRequest.Status,
                formRequest.Comments,
                formRequest.AppliedRecordKey,
                formRequest.FailureMessage
            });

            // Track detailed field changes
            var fieldChanges = GetDetailedFieldChanges(currentRequest, formRequest);
            
            // Record the changes in history
            var previousValues = new Dictionary<string, object?>
            {
                { "RequestType", currentRequest.RequestType },
                { "Status", currentRequest.Status.ToString() },
                { "Comments", currentRequest.Comments }
            };

            var newValues = new Dictionary<string, object?>
            {
                { "RequestType", formRequest.RequestType },
                { "Status", formRequest.Status.ToString() },
                { "Comments", formRequest.Comments }
            };

            // Add field changes to the tracking - store as a list of change objects
            if (fieldChanges.Any())
            {
                var changesList = fieldChanges.Select(c => new
                {
                    FieldName = c.FieldName,
                    PreviousValue = c.PreviousValue?.ToString(),
                    NewValue = c.NewValue?.ToString(),
                    ChangeType = c.ChangeType
                }).ToList();
                
                newValues["FieldChanges"] = changesList;
            }

            // Determine the change type
            var changeType = currentRequest.Status != formRequest.Status 
                ? FormRequestChangeType.StatusChanged 
                : FormRequestChangeType.Updated;

            await RecordChangeAsync(
                formRequest.Id,
                changeType,
                previousValues,
                newValues,
                formRequest.RequestedBy, // In a real scenario, this should be the current user making the change
                formRequest.RequestedByName,
                $"Request updated{(fieldChanges.Any() ? $" - {fieldChanges.Count} field(s) changed" : "")}"
            );

            return formRequest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating form request {Id}", formRequest.Id);
            throw;
        }
    }

    private List<FieldChange> GetDetailedFieldChanges(FormRequest currentRequest, FormRequest updatedRequest)
    {
        var changes = new List<FieldChange>();

        // Compare FieldValues
        var allFieldKeys = currentRequest.FieldValues.Keys
            .Union(updatedRequest.FieldValues.Keys)
            .ToHashSet();

        foreach (var fieldKey in allFieldKeys)
        {
            var currentValue = currentRequest.FieldValues.GetValueOrDefault(fieldKey);
            var newValue = updatedRequest.FieldValues.GetValueOrDefault(fieldKey);

            if (!AreValuesEqual(currentValue, newValue))
            {
                changes.Add(new FieldChange
                {
                    FieldName = fieldKey,
                    PreviousValue = currentValue,
                    NewValue = newValue,
                    ChangeType = "FieldValue",
                    HasPreviousValue = currentValue != null,
                    HasNewValue = newValue != null
                });
            }
        }

        // Compare OriginalValues
        var allOriginalKeys = currentRequest.OriginalValues.Keys
            .Union(updatedRequest.OriginalValues.Keys)
            .ToHashSet();

        foreach (var fieldKey in allOriginalKeys)
        {
            var currentValue = currentRequest.OriginalValues.GetValueOrDefault(fieldKey);
            var newValue = updatedRequest.OriginalValues.GetValueOrDefault(fieldKey);

            if (!AreValuesEqual(currentValue, newValue))
            {
                changes.Add(new FieldChange
                {
                    FieldName = $"Original_{fieldKey}",
                    PreviousValue = currentValue,
                    NewValue = newValue,
                    ChangeType = "OriginalValue",
                    HasPreviousValue = currentValue != null,
                    HasNewValue = newValue != null
                });
            }
        }

        return changes;
    }

    private bool AreValuesEqual(object? value1, object? value2)
    {
        if (value1 == null && value2 == null) return true;
        if (value1 == null || value2 == null) return false;
        
        // Convert to strings for comparison to handle different types
        var str1 = value1.ToString();
        var str2 = value2.ToString();
        
        return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
    }

    // Helper method for debugging field changes (can be removed in production)
    public async Task<string> GetDebugInfoAsync(int formRequestId)
    {
        try
        {
            var history = await GetFormRequestHistoryAsync(formRequestId);
            var debugInfo = new System.Text.StringBuilder();
            
            foreach (var historyItem in history)
            {
                debugInfo.AppendLine($"History Item {historyItem.Id}: {historyItem.ChangeType}");
                debugInfo.AppendLine($"  PreviousValues JSON: {JsonSerializer.Serialize(historyItem.PreviousValues)}");
                debugInfo.AppendLine($"  NewValues JSON: {JsonSerializer.Serialize(historyItem.NewValues)}");
                debugInfo.AppendLine();
            }
            
            return debugInfo.ToString();
        }
        catch (Exception ex)
        {
            return $"Error getting debug info: {ex.Message}";
        }
    }

    public async Task<bool> ApproveFormRequestAsync(int id, string approvedBy, string approvedByName)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // First, get the form request details
            var formRequest = await GetFormRequestAsync(id);
            if (formRequest == null || formRequest.Status != RequestStatus.Pending)
            {
                await transaction.RollbackAsync();
                return false;
            }

            // Update the request status to approved
            var sql = @"
                UPDATE FormRequests 
                SET Status = @Status, ApprovedBy = @ApprovedBy, ApprovedByName = @ApprovedByName, ApprovedAt = @ApprovedAt
                WHERE Id = @Id";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                Id = id,
                Status = (int)RequestStatus.Approved,
                ApprovedBy = approvedBy,
                ApprovedByName = approvedByName,
                ApprovedAt = DateTime.UtcNow
            }, transaction);

            if (rowsAffected == 0)
            {
                await transaction.RollbackAsync();
                return false;
            }

            // Record the approval in history
            await RecordChangeWithTransactionAsync(
                connection,
                transaction,
                id,
                FormRequestChangeType.Approved,
                new Dictionary<string, object?> { { "Status", "Pending" } },
                new Dictionary<string, object?> { { "Status", "Approved" } },
                approvedBy,
                approvedByName,
                "Request approved"
            );

            // Now automatically apply the changes to the target database
            try
            {
                var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formRequest.FormDefinitionId);
                if (formDefinition == null)
                {
                    throw new InvalidOperationException($"Form definition with ID {formRequest.FormDefinitionId} not found");
                }

                // Apply the changes to the target database
                bool applicationSuccess = false;
                object? insertedId = null;

                // Convert JsonElement objects to proper types for database operations
                var convertedFieldValues = ConvertJsonElementsToValues(formRequest.FieldValues);
                var convertedOriginalValues = ConvertJsonElementsToValues(formRequest.OriginalValues);

                switch (formRequest.RequestType)
                {
                    case RequestType.Insert:
                        try
                        {
                            var (success, recordKey) = await _dataService.InsertDataWithIdAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema,
                                convertedFieldValues
                            );
                            applicationSuccess = success;
                            insertedId = recordKey;
                        }
                        catch (Exception insertEx)
                        {
                            _logger.LogError(insertEx, "INSERT operation failed for form request {Id}. FieldValues: {FieldValues}", 
                                id, 
                                string.Join(", ", convertedFieldValues.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                            throw new InvalidOperationException($"INSERT operation failed: {insertEx.Message}", insertEx);
                        }
                        break;

                    case RequestType.Update:
                        try
                        {
                            // Get primary key columns for the table
                            var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema
                            );

                            if (!primaryKeyColumns.Any())
                            {
                                throw new InvalidOperationException($"No primary key found for table {formDefinition.Schema}.{formDefinition.TableName}. Cannot perform UPDATE operation.");
                            }

                            // Create WHERE conditions using only primary key fields from original values
                            var whereConditions = new Dictionary<string, object?>();
                            foreach (var pkColumn in primaryKeyColumns)
                            {
                                if (convertedOriginalValues.ContainsKey(pkColumn))
                                {
                                    whereConditions[pkColumn] = convertedOriginalValues[pkColumn];
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values. Cannot perform UPDATE operation.");
                                }
                            }

                            _logger.LogInformation("Performing UPDATE on {Database}.{Schema}.{Table} using primary key WHERE conditions: {WhereConditions}",
                                formDefinition.DatabaseConnectionName, formDefinition.Schema, formDefinition.TableName,
                                string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}")));

                            applicationSuccess = await _dataService.UpdateDataAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema,
                                convertedFieldValues,
                                whereConditions  // Use only primary key fields for WHERE clause
                            );
                            
                            // For UPDATE operations, set the "record key" to the primary key values that were used
                            if (applicationSuccess)
                            {
                                insertedId = string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                            }
                        }
                        catch (Exception updateEx)
                        {
                            _logger.LogError(updateEx, "UPDATE operation failed for form request {Id}. FieldValues: {FieldValues}, OriginalValues: {OriginalValues}", 
                                id, 
                                string.Join(", ", convertedFieldValues.Select(kvp => $"{kvp.Key}={kvp.Value}")),
                                string.Join(", ", convertedOriginalValues.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                            throw new InvalidOperationException($"UPDATE operation failed: {updateEx.Message}", updateEx);
                        }
                        break;

                    case RequestType.Delete:
                        try
                        {
                            // Get primary key columns for the table
                            var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema
                            );

                            if (!primaryKeyColumns.Any())
                            {
                                throw new InvalidOperationException($"No primary key found for table {formDefinition.Schema}.{formDefinition.TableName}. Cannot perform DELETE operation.");
                            }

                            // Create WHERE conditions using only primary key fields from original values
                            var whereConditions = new Dictionary<string, object?>();
                            foreach (var pkColumn in primaryKeyColumns)
                            {
                                if (convertedOriginalValues.ContainsKey(pkColumn))
                                {
                                    whereConditions[pkColumn] = convertedOriginalValues[pkColumn];
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values. Cannot perform DELETE operation.");
                                }
                            }

                            _logger.LogInformation("Performing DELETE on {Database}.{Schema}.{Table} using primary key WHERE conditions: {WhereConditions}",
                                formDefinition.DatabaseConnectionName, formDefinition.Schema, formDefinition.TableName,
                                string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}")));

                            applicationSuccess = await _dataService.DeleteDataAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema,
                                whereConditions  // Use only primary key fields for WHERE clause
                            );
                            
                            // For DELETE operations, set the "record key" to the primary key values that were used
                            if (applicationSuccess)
                            {
                                insertedId = string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                            }
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogError(deleteEx, "DELETE operation failed for form request {Id}. OriginalValues: {OriginalValues}", 
                                id, 
                                string.Join(", ", convertedOriginalValues.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                            throw new InvalidOperationException($"DELETE operation failed: {deleteEx.Message}", deleteEx);
                        }
                        break;
                }

                if (applicationSuccess)
                {
                    // Update the request status to Applied and store the record key
                    var applySql = @"
                        UPDATE FormRequests 
                        SET Status = @Status, AppliedRecordKey = @AppliedRecordKey, FailureMessage = NULL
                        WHERE Id = @Id";

                    await connection.ExecuteAsync(applySql, new
                    {
                        Id = id,
                        Status = (int)RequestStatus.Applied,
                        AppliedRecordKey = insertedId?.ToString()
                    }, transaction);

                    // Record the successful application in history
                    string successComment = formRequest.RequestType switch
                    {
                        RequestType.Insert => $"Record successfully inserted into target database. New record key: {insertedId}",
                        RequestType.Update => $"Record successfully updated in target database. Updated record: {insertedId}",
                        RequestType.Delete => $"Record successfully deleted from target database. Deleted record: {insertedId}",
                        _ => $"Request successfully applied to target database. Record key: {insertedId}"
                    };

                    await RecordChangeWithTransactionAsync(
                        connection,
                        transaction,
                        id,
                        FormRequestChangeType.Applied,
                        new Dictionary<string, object?> { { "Status", "Approved" } },
                        new Dictionary<string, object?> 
                        { 
                            { "Status", "Applied" },
                            { "AppliedRecordKey", insertedId?.ToString() },
                            { "OperationType", formRequest.RequestType }
                        },
                        "System",
                        "System",
                        successComment
                    );

                    _logger.LogInformation("Form request {Id} approved and successfully applied to {Database}.{Schema}.{Table}. Record key: {RecordKey}",
                        id, formDefinition.DatabaseConnectionName, formDefinition.Schema, formDefinition.TableName, insertedId);
                }
                else
                {
                    throw new InvalidOperationException($"Failed to apply {formRequest.RequestType} operation to target database");
                }
            }
            catch (Exception applyEx)
            {
                _logger.LogError(applyEx, "Failed to apply form request {Id} to target database after approval", id);

                // Update the request status to Failed and store the error message
                var failSql = @"
                    UPDATE FormRequests 
                    SET Status = @Status, FailureMessage = @FailureMessage
                    WHERE Id = @Id";

                await connection.ExecuteAsync(failSql, new
                {
                    Id = id,
                    Status = (int)RequestStatus.Failed,
                    FailureMessage = applyEx.Message
                }, transaction);

                // Record the failure in history
                await RecordChangeWithTransactionAsync(
                    connection,
                    transaction,
                    id,
                    FormRequestChangeType.Failed,
                    new Dictionary<string, object?> { { "Status", "Approved" } },
                    new Dictionary<string, object?> 
                    { 
                        { "Status", "Failed" },
                        { "FailureMessage", applyEx.Message }
                    },
                    "System",
                    "System",
                    $"Failed to apply request to target database: {applyEx.Message}"
                );
            }

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error approving form request {Id}", id);
            throw;
        }
    }

    public async Task<bool> RejectFormRequestAsync(int id, string rejectedBy, string rejectedByName, string reason)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                UPDATE FormRequests 
                SET Status = @Status, ApprovedBy = @RejectedBy, ApprovedByName = @RejectedByName, ApprovedAt = @RejectedAt, RejectionReason = @RejectionReason
                WHERE Id = @Id";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                Id = id,
                Status = (int)RequestStatus.Rejected,
                RejectedBy = rejectedBy,
                RejectedByName = rejectedByName,
                RejectedAt = DateTime.UtcNow,
                RejectionReason = reason
            });

            if (rowsAffected > 0)
            {
                // Record the rejection in history
                await RecordChangeAsync(
                    id,
                    FormRequestChangeType.Rejected,
                    new Dictionary<string, object?> { { "Status", "Pending" } },
                    new Dictionary<string, object?> { { "Status", "Rejected" }, { "RejectionReason", reason } },
                    rejectedBy,
                    rejectedByName,
                    $"Request rejected: {reason}"
                );
            }

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting form request {Id}", id);
            throw;
        }
    }

    public async Task<bool> ApplyFormRequestAsync(int id)
    {
        try
        {
            var formRequest = await GetFormRequestAsync(id);
            if (formRequest == null || formRequest.Status != RequestStatus.Approved)
            {
                return false;
            }

            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formRequest.FormDefinitionId);
            if (formDefinition == null)
            {
                return false;
            }

            // Apply the changes to the target database
            bool success = false;
            
            // Convert JsonElement objects to proper types for database operations
            var convertedFieldValues = ConvertJsonElementsToValues(formRequest.FieldValues);
            var convertedOriginalValues = ConvertJsonElementsToValues(formRequest.OriginalValues);
            
            switch (formRequest.RequestType)
            {
                case RequestType.Insert:
                    success = await _dataService.InsertDataAsync(
                        formDefinition.DatabaseConnectionName,
                        formDefinition.TableName,
                        formDefinition.Schema,
                        convertedFieldValues
                    );
                    break;
                case RequestType.Update:
                    // Get primary key columns for the table
                    var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                        formDefinition.DatabaseConnectionName,
                        formDefinition.TableName,
                        formDefinition.Schema
                    );

                    if (!primaryKeyColumns.Any())
                    {
                        throw new InvalidOperationException($"No primary key found for table {formDefinition.Schema}.{formDefinition.TableName}. Cannot perform UPDATE operation.");
                    }

                    // Create WHERE conditions using only primary key fields from original values
                    var updateWhereConditions = new Dictionary<string, object?>();
                    foreach (var pkColumn in primaryKeyColumns)
                    {
                        if (convertedOriginalValues.ContainsKey(pkColumn))
                        {
                            updateWhereConditions[pkColumn] = convertedOriginalValues[pkColumn];
                        }
                        else
                        {
                            throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values. Cannot perform UPDATE operation.");
                        }
                    }

                    success = await _dataService.UpdateDataAsync(
                        formDefinition.DatabaseConnectionName,
                        formDefinition.TableName,
                        formDefinition.Schema,
                        convertedFieldValues,
                        updateWhereConditions
                    );
                    break;
                case RequestType.Delete:
                    // Get primary key columns for the table
                    var deletePrimaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                        formDefinition.DatabaseConnectionName,
                        formDefinition.TableName,
                        formDefinition.Schema
                    );

                    if (!deletePrimaryKeyColumns.Any())
                    {
                        throw new InvalidOperationException($"No primary key found for table {formDefinition.Schema}.{formDefinition.TableName}. Cannot perform DELETE operation.");
                    }

                    // Create WHERE conditions using only primary key fields from original values
                    var deleteWhereConditions = new Dictionary<string, object?>();
                    foreach (var pkColumn in deletePrimaryKeyColumns)
                    {
                        if (convertedOriginalValues.ContainsKey(pkColumn))
                        {
                            deleteWhereConditions[pkColumn] = convertedOriginalValues[pkColumn];
                        }
                        else
                        {
                            throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values. Cannot perform DELETE operation.");
                        }
                    }

                    success = await _dataService.DeleteDataAsync(
                        formDefinition.DatabaseConnectionName,
                        formDefinition.TableName,
                        formDefinition.Schema,
                        deleteWhereConditions
                    );
                    break;
            }

            if (success)
            {
                // Update the request status to Applied
                using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = "UPDATE FormRequests SET Status = @Status WHERE Id = @Id";
                await connection.ExecuteAsync(sql, new { Id = id, Status = (int)RequestStatus.Applied });

                // Record the application in history
                await RecordChangeAsync(
                    id,
                    FormRequestChangeType.Applied,
                    new Dictionary<string, object?> { { "Status", "Approved" } },
                    new Dictionary<string, object?> { { "Status", "Applied" } },
                    "System", // In a real scenario, this should be the current user
                    "System",
                    "Request applied to target database"
                );
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying form request {Id}", id);
            throw;
        }
    }

    public async Task<List<int>> GetApprovedButNotAppliedRequestIdsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT Id 
            FROM FormRequests 
            WHERE Status = @Status 
            ORDER BY Id";

        var requestIds = await connection.QueryAsync<int>(sql, new { Status = (int)RequestStatus.Approved });
        return requestIds.ToList();
    }

    public async Task<bool> ManuallyApplyApprovedRequestAsync(int id)
    {
        try
        {
            _logger.LogInformation("Manually applying approved form request {FormRequestId}", id);
            
            // Get the form request
            var formRequest = await GetFormRequestAsync(id);
            if (formRequest == null || formRequest.Status != RequestStatus.Approved)
            {
                _logger.LogWarning("Form request {FormRequestId} not found or not in approved status", id);
                return false;
            }

            // Use the existing ApplyFormRequestAsync method
            var success = await ApplyFormRequestAsync(id);
            
            if (success)
            {
                _logger.LogInformation("Successfully manually applied form request {FormRequestId}", id);
            }
            else
            {
                _logger.LogError("Failed to manually apply form request {FormRequestId}", id);
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error manually applying form request {FormRequestId}", id);
            return false;
        }
    }

    // Alias methods for UI compatibility
    public async Task<FormRequest?> GetByIdAsync(int id)
    {
        return await GetFormRequestAsync(id);
    }
    
    public async Task<FormRequest> CreateAsync(FormRequest formRequest)
    {
        return await CreateFormRequestAsync(formRequest);
    }
    
    public async Task<FormRequest> UpdateAsync(FormRequest formRequest)
    {
        return await UpdateFormRequestAsync(formRequest);
    }
    
    public async Task<List<FormRequest>> GetByUserAsync(string userId)
    {
        return await GetFormRequestsByUserAsync(userId);
    }
    
    public async Task<List<FormRequest>> GetPendingApprovalsAsync(string userId)
    {
        // Get user roles from context or configuration
        // For now, assume user is an admin and can see all pending requests
        // In production, this should check user's Entra ID groups/roles
        var userRoles = new List<string> { "Admin" }; // This should come from the user's actual roles
        
        return await GetFormRequestsForApprovalAsync(userRoles);
    }
    
    public async Task<bool> ApproveAsync(int id, string approvedBy, string? comments = null)
    {
        return await ApproveFormRequestAsync(id, approvedBy, approvedBy);
    }
    
    public async Task<bool> RejectAsync(int id, string rejectedBy, string? comments = null)
    {
        var reason = comments ?? "Request rejected";
        return await RejectFormRequestAsync(id, rejectedBy, rejectedBy, reason);
    }

    // Change tracking methods
    public async Task<List<FormRequestHistory>> GetFormRequestHistoryAsync(int formRequestId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT Id, FormRequestId, ChangeType, PreviousValues as PreviousValuesJson, 
                       NewValues as NewValuesJson, ChangedBy, ChangedByName, ChangedAt, Comments
                FROM FormRequestHistory
                WHERE FormRequestId = @FormRequestId
                ORDER BY ChangedAt DESC";

            var historyItems = await connection.QueryAsync(sql, new { FormRequestId = formRequestId });
            var result = new List<FormRequestHistory>();

            foreach (var row in historyItems)
            {
                var history = new FormRequestHistory
                {
                    Id = (int)row.Id,
                    FormRequestId = (int)row.FormRequestId,
                    ChangeType = ParseChangeType((string)row.ChangeType),
                    PreviousValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.PreviousValuesJson ?? "{}")) ?? new Dictionary<string, object?>(),
                    NewValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.NewValuesJson ?? "{}")) ?? new Dictionary<string, object?>(),
                    ChangedBy = (string)row.ChangedBy,
                    ChangedByName = (string)row.ChangedByName,
                    ChangedAt = (DateTime)row.ChangedAt,
                    Comments = (string?)row.Comments
                };
                result.Add(history);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form request history for request {FormRequestId}", formRequestId);
            throw;
        }
    }

    public async Task<FormRequestHistory> AddFormRequestHistoryAsync(FormRequestHistory history)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT INTO FormRequestHistory (FormRequestId, ChangeType, PreviousValues, NewValues, ChangedBy, ChangedByName, ChangedAt, Comments)
                OUTPUT INSERTED.Id
                VALUES (@FormRequestId, @ChangeType, @PreviousValues, @NewValues, @ChangedBy, @ChangedByName, @ChangedAt, @Comments)";

            var id = await connection.QuerySingleAsync<int>(sql, new
            {
                history.FormRequestId,
                ChangeType = ChangeTypeToString(history.ChangeType),
                PreviousValues = JsonSerializer.Serialize(history.PreviousValues),
                NewValues = JsonSerializer.Serialize(history.NewValues),
                history.ChangedBy,
                history.ChangedByName,
                history.ChangedAt,
                history.Comments
            });

            history.Id = id;
            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding form request history");
            throw;
        }
    }

    public async Task<FormRequest?> GetFormRequestWithHistoryAsync(int id)
    {
        try
        {
            var request = await GetFormRequestAsync(id);
            if (request != null)
            {
                request.History = await GetFormRequestHistoryAsync(id);
            }
            return request;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form request with history {Id}", id);
            throw;
        }
    }

    private async Task RecordChangeWithTransactionAsync(SqlConnection connection, SqlTransaction transaction, int formRequestId, FormRequestChangeType changeType, 
        Dictionary<string, object?>? previousValues, Dictionary<string, object?>? newValues, 
        string changedBy, string changedByName, string? comments = null)
    {
        var sql = @"
            INSERT INTO FormRequestHistory (FormRequestId, ChangeType, PreviousValues, NewValues, ChangedBy, ChangedByName, ChangedAt, Comments)
            VALUES (@FormRequestId, @ChangeType, @PreviousValues, @NewValues, @ChangedBy, @ChangedByName, @ChangedAt, @Comments)";

        await connection.ExecuteAsync(sql, new
        {
            FormRequestId = formRequestId,
            ChangeType = changeType.ToString(),
            PreviousValues = JsonSerializer.Serialize(previousValues ?? new Dictionary<string, object?>()),
            NewValues = JsonSerializer.Serialize(newValues ?? new Dictionary<string, object?>()),
            ChangedBy = changedBy,
            ChangedByName = changedByName,
            ChangedAt = DateTime.UtcNow,
            Comments = comments
        }, transaction);
    }

    private async Task RecordChangeAsync(int formRequestId, FormRequestChangeType changeType, 
        Dictionary<string, object?>? previousValues, Dictionary<string, object?>? newValues, 
        string changedBy, string changedByName, string? comments = null)
    {
        var history = new FormRequestHistory
        {
            FormRequestId = formRequestId,
            ChangeType = changeType,
            PreviousValues = previousValues ?? new Dictionary<string, object?>(),
            NewValues = newValues ?? new Dictionary<string, object?>(),
            ChangedBy = changedBy,
            ChangedByName = changedByName,
            ChangedAt = DateTime.UtcNow,
            Comments = comments
        };

        await AddFormRequestHistoryAsync(history);
    }

    private Dictionary<string, object?> ConvertJsonElementsToValues(Dictionary<string, object?> data)
    {
        var result = new Dictionary<string, object?>();
        
        foreach (var kvp in data)
        {
            if (kvp.Value is JsonElement jsonElement)
            {
                result[kvp.Key] = ConvertJsonElement(jsonElement);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        
        return result;
    }

    private object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => ConvertStringValue(element.GetString()),
            JsonValueKind.Number => element.TryGetInt32(out int intValue) ? intValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Convert a string value to its appropriate .NET type, including DateTime handling
    /// </summary>
    private static object? ConvertStringValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Try to parse as DateTime first (common serialization format)
        if (DateTime.TryParse(value, out var dateTime))
        {
            return dateTime;
        }

        // Try to parse as DateTimeOffset (ISO 8601 format)
        if (DateTimeOffset.TryParse(value, out var dateTimeOffset))
        {
            return dateTimeOffset.DateTime;
        }

        // Return as string if no special conversion needed
        return value;
    }

    public async Task<bool> RetryFailedFormRequestAsync(int id, string retriedBy, string retriedByName)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction();

        try
        {
            // Get the form request details
            var formRequest = await GetFormRequestAsync(id);
            if (formRequest == null || formRequest.Status != RequestStatus.Failed)
            {
                await transaction.RollbackAsync();
                return false;
            }

            var formDefinition = await _formDefinitionService.GetFormDefinitionAsync(formRequest.FormDefinitionId);
            if (formDefinition == null)
            {
                await transaction.RollbackAsync();
                return false;
            }

            // Reset the request status to Approved and clear the failure message
            var resetSql = @"
                UPDATE FormRequests 
                SET Status = @Status, FailureMessage = NULL
                WHERE Id = @Id";

            await connection.ExecuteAsync(resetSql, new
            {
                Id = id,
                Status = (int)RequestStatus.Approved
            }, transaction);

            // Record the retry attempt in history
            await RecordChangeWithTransactionAsync(
                connection,
                transaction,
                id,
                FormRequestChangeType.StatusChanged,
                new Dictionary<string, object?> 
                { 
                    { "Status", "Failed" },
                    { "FailureMessage", formRequest.FailureMessage }
                },
                new Dictionary<string, object?> { { "Status", "Approved" } },
                retriedBy,
                retriedByName,
                "Request retry initiated - attempting to apply changes to target database again"
            );

            // Now try to apply the changes to the target database again
            try
            {
                bool applicationSuccess = false;
                object? insertedId = null;

                // Convert JsonElement objects to proper types for database operations
                var convertedFieldValues = ConvertJsonElementsToValues(formRequest.FieldValues);
                var convertedOriginalValues = ConvertJsonElementsToValues(formRequest.OriginalValues);

                switch (formRequest.RequestType)
                {
                    case RequestType.Insert:
                        try
                        {
                            var (success, recordKey) = await _dataService.InsertDataWithIdAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema,
                                convertedFieldValues
                            );
                            applicationSuccess = success;
                            insertedId = recordKey;
                        }
                        catch (Exception insertEx)
                        {
                            _logger.LogError(insertEx, "INSERT operation failed during retry for form request {Id}. FieldValues: {FieldValues}", 
                                id, 
                                string.Join(", ", convertedFieldValues.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                            throw new InvalidOperationException($"INSERT operation failed: {insertEx.Message}", insertEx);
                        }
                        break;

                    case RequestType.Update:
                        try
                        {
                            // Get primary key columns for the table
                            var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema
                            );

                            if (!primaryKeyColumns.Any())
                            {
                                throw new InvalidOperationException($"No primary key found for table {formDefinition.Schema}.{formDefinition.TableName}. Cannot perform UPDATE operation.");
                            }

                            // Create WHERE conditions using only primary key fields from original values
                            var whereConditions = new Dictionary<string, object?>();
                            foreach (var pkColumn in primaryKeyColumns)
                            {
                                if (convertedOriginalValues.ContainsKey(pkColumn))
                                {
                                    whereConditions[pkColumn] = convertedOriginalValues[pkColumn];
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values. Cannot perform UPDATE operation.");
                                }
                            }

                            applicationSuccess = await _dataService.UpdateDataAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema,
                                convertedFieldValues,
                                whereConditions
                            );
                            
                            if (applicationSuccess)
                            {
                                insertedId = string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                            }
                        }
                        catch (Exception updateEx)
                        {
                            _logger.LogError(updateEx, "UPDATE operation failed during retry for form request {Id}. FieldValues: {FieldValues}, OriginalValues: {OriginalValues}", 
                                id, 
                                string.Join(", ", convertedFieldValues.Select(kvp => $"{kvp.Key}={kvp.Value}")),
                                string.Join(", ", convertedOriginalValues.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                            throw new InvalidOperationException($"UPDATE operation failed: {updateEx.Message}", updateEx);
                        }
                        break;

                    case RequestType.Delete:
                        try
                        {
                            // Get primary key columns for the table
                            var primaryKeyColumns = await _dataService.GetPrimaryKeyColumnsAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema
                            );

                            if (!primaryKeyColumns.Any())
                            {
                                throw new InvalidOperationException($"No primary key found for table {formDefinition.Schema}.{formDefinition.TableName}. Cannot perform DELETE operation.");
                            }

                            // Create WHERE conditions using only primary key fields from original values
                            var whereConditions = new Dictionary<string, object?>();
                            foreach (var pkColumn in primaryKeyColumns)
                            {
                                if (convertedOriginalValues.ContainsKey(pkColumn))
                                {
                                    whereConditions[pkColumn] = convertedOriginalValues[pkColumn];
                                }
                                else
                                {
                                    throw new InvalidOperationException($"Primary key column '{pkColumn}' not found in original values. Cannot perform DELETE operation.");
                                }
                            }

                            applicationSuccess = await _dataService.DeleteDataAsync(
                                formDefinition.DatabaseConnectionName,
                                formDefinition.TableName,
                                formDefinition.Schema,
                                whereConditions
                            );
                            
                            if (applicationSuccess)
                            {
                                insertedId = string.Join(", ", whereConditions.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                            }
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogError(deleteEx, "DELETE operation failed during retry for form request {Id}. OriginalValues: {OriginalValues}", 
                                id, 
                                string.Join(", ", convertedOriginalValues.Select(kvp => $"{kvp.Key}={kvp.Value}")));
                            throw new InvalidOperationException($"DELETE operation failed: {deleteEx.Message}", deleteEx);
                        }
                        break;
                }

                if (applicationSuccess)
                {
                    // Update the request status to Applied and store the record key
                    var applySql = @"
                        UPDATE FormRequests 
                        SET Status = @Status, AppliedRecordKey = @AppliedRecordKey, FailureMessage = NULL
                        WHERE Id = @Id";

                    await connection.ExecuteAsync(applySql, new
                    {
                        Id = id,
                        Status = (int)RequestStatus.Applied,
                        AppliedRecordKey = insertedId?.ToString()
                    }, transaction);

                    // Record the successful application in history
                    string successComment = formRequest.RequestType switch
                    {
                        RequestType.Insert => $"Record successfully inserted into target database after retry. New record key: {insertedId}",
                        RequestType.Update => $"Record successfully updated in target database after retry. Updated record: {insertedId}",
                        RequestType.Delete => $"Record successfully deleted from target database after retry. Deleted record: {insertedId}",
                        _ => $"Request successfully applied to target database after retry. Record key: {insertedId}"
                    };

                    await RecordChangeWithTransactionAsync(
                        connection,
                        transaction,
                        id,
                        FormRequestChangeType.Applied,
                        new Dictionary<string, object?> { { "Status", "Approved" } },
                        new Dictionary<string, object?> 
                        { 
                            { "Status", "Applied" },
                            { "AppliedRecordKey", insertedId?.ToString() },
                            { "OperationType", formRequest.RequestType },
                            { "RetryAttempt", true }
                        },
                        retriedBy,
                        retriedByName,
                        successComment
                    );

                    _logger.LogInformation("Form request {Id} successfully retried and applied to {Database}.{Schema}.{Table}. Record key: {RecordKey}",
                        id, formDefinition.DatabaseConnectionName, formDefinition.Schema, formDefinition.TableName, insertedId);
                }
                else
                {
                    throw new InvalidOperationException($"Failed to apply {formRequest.RequestType} operation to target database during retry");
                }
            }
            catch (Exception applyEx)
            {
                _logger.LogError(applyEx, "Failed to apply form request {Id} to target database during retry", id);

                // Update the request status back to Failed and store the new error message
                var failSql = @"
                    UPDATE FormRequests 
                    SET Status = @Status, FailureMessage = @FailureMessage
                    WHERE Id = @Id";

                await connection.ExecuteAsync(failSql, new
                {
                    Id = id,
                    Status = (int)RequestStatus.Failed,
                    FailureMessage = $"Retry attempt failed: {applyEx.Message}"
                }, transaction);

                // Record the retry failure in history
                await RecordChangeWithTransactionAsync(
                    connection,
                    transaction,
                    id,
                    FormRequestChangeType.Failed,
                    new Dictionary<string, object?> { { "Status", "Approved" } },
                    new Dictionary<string, object?> 
                    { 
                        { "Status", "Failed" },
                        { "FailureMessage", $"Retry attempt failed: {applyEx.Message}" },
                        { "RetryAttempt", true }
                    },
                    retriedBy,
                    retriedByName,
                    $"Retry attempt failed to apply request to target database: {applyEx.Message}"
                );

                await transaction.RollbackAsync();
                return false;
            }

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error retrying failed form request {Id}", id);
            throw;
        }
    }

    // Workflow integration methods
    public async Task<List<FormRequest>> GetFormRequestsForWorkflowApprovalAsync(string userId, List<string> userRoles)
    {
        try
        {
            _logger.LogInformation("GetFormRequestsForWorkflowApprovalAsync called for userId: {UserId}, userRoles: {UserRoles}", 
                userId, string.Join(", ", userRoles));

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT DISTINCT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                       fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, fr.RequestedByName, 
                       fr.RequestedAt, fr.ApprovedBy, fr.ApprovedByName, fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                       fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId,
                       fd.Name as FormName, fd.Description as FormDescription,
                       wsi.Status as WorkflowStepStatus, ws.AssignedRoles, ws.Name as StepName, wsi.Id as WorkflowStepInstanceId
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                INNER JOIN WorkflowInstances wi ON fr.WorkflowInstanceId = wi.Id
                INNER JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId
                INNER JOIN WorkflowSteps ws ON wsi.StepId = ws.StepId AND wi.WorkflowDefinitionId = ws.WorkflowDefinitionId
                WHERE ws.StepType = @ApprovalStepType
                  AND wsi.Status IN (@PendingStatus, @InProgressStatus, @CompletedStatus)
                  AND (@IsAdmin = 1 
                       OR (ws.AssignedRoles IS NOT NULL 
                           AND ws.AssignedRoles != '[]' 
                           AND JSON_QUERY(ws.AssignedRoles, '$') IS NOT NULL 
                           AND EXISTS (
                               SELECT 1 FROM OPENJSON(ws.AssignedRoles) AS roles
                               WHERE roles.value IN @UserRoles
                           )))
                ORDER BY fr.RequestedAt DESC";

            var parameters = new DynamicParameters();
            parameters.Add("ApprovalStepType", WorkflowStepType.Approval.ToString());
            parameters.Add("PendingStatus", (int)WorkflowStepInstanceStatus.Pending);
            parameters.Add("InProgressStatus", (int)WorkflowStepInstanceStatus.InProgress);
            parameters.Add("CompletedStatus", (int)WorkflowStepInstanceStatus.Completed);
            parameters.Add("UserRoles", userRoles);
            
            // Check if user is admin - they can see all approvals
            var isAdmin = userRoles.Contains("Admin");
            parameters.Add("IsAdmin", isAdmin ? 1 : 0);

            _logger.LogInformation("Executing SQL query for Approval steps only with integer status values: Pending={PendingStatus}, InProgress={InProgressStatus}, Completed={CompletedStatus}, IsAdmin: {IsAdmin}, UserRoles: {UserRoles}", 
                (int)WorkflowStepInstanceStatus.Pending,
                (int)WorkflowStepInstanceStatus.InProgress, 
                (int)WorkflowStepInstanceStatus.Completed,
                isAdmin, 
                string.Join(", ", userRoles));

            var requests = await connection.QueryAsync(sql, parameters);
            
            _logger.LogInformation("Raw SQL query returned {Count} rows", requests.Count());

            var result = new List<FormRequest>();

            foreach (var row in requests)
            {
                var request = CreateFormRequestFromRow(row);
                request.WorkflowInstanceId = (int?)((IDictionary<string, object>)row)["WorkflowInstanceId"];
                result.Add(request);
            }

            _logger.LogInformation("Returning {Count} form requests for workflow approval", result.Count);

            _logger.LogInformation("Returning {Count} form requests for workflow approval", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for workflow approval for user {UserId} with roles {Roles}", userId, string.Join(", ", userRoles));
            throw;
        }
    }

    public async Task<bool> ProcessWorkflowActionAsync(int formRequestId, string actionType, string userId, string? comments = null, Dictionary<string, object?>? fieldUpdates = null)
    {
        try
        {
            // Get the form request and current workflow step
            var formRequest = await GetFormRequestAsync(formRequestId);
            if (formRequest?.WorkflowInstanceId == null)
            {
                _logger.LogWarning("Form request {FormRequestId} does not have an active workflow", formRequestId);
                return false;
            }

            // Process the workflow action
            var result = await _workflowService.ProcessWorkflowActionAsync(
                formRequest.WorkflowInstanceId.Value,
                actionType,
                userId,
                comments,
                fieldUpdates);

            if (!result.Success)
            {
                _logger.LogWarning("Workflow action {ActionType} failed for form request {FormRequestId}: {Message}", 
                    actionType, formRequestId, result.Message);
                return false;
            }

            // Update form request status based on workflow status
            if (result.WorkflowCompleted)
            {
                formRequest.Status = result.WorkflowApproved ? RequestStatus.Approved : RequestStatus.Rejected;
                
                // Note: Auto-application is now handled elsewhere to avoid circular dependencies
                // if (result.WorkflowApproved)
                // {
                //     await ApplyFormRequestAsync(formRequestId);
                // }
            }

            // Apply any field updates
            if (fieldUpdates?.Any() == true)
            {
                foreach (var update in fieldUpdates)
                {
                    formRequest.FieldValues[update.Key] = update.Value;
                }
                await UpdateFormRequestAsync(formRequest);
            }

            // Record the action in form request history
            await RecordChangeAsync(
                formRequestId,
                actionType.ToLower() switch
                {
                    "approve" => FormRequestChangeType.Approved,
                    "reject" => FormRequestChangeType.Rejected,
                    _ => FormRequestChangeType.StatusChanged
                },
                new Dictionary<string, object?> { { "PreviousStep", result.PreviousStepName } },
                new Dictionary<string, object?> 
                { 
                    { "CurrentStep", result.CurrentStepName },
                    { "ActionType", actionType },
                    { "Comments", comments },
                    { "FieldUpdates", fieldUpdates }
                },
                userId,
                result.ActorName ?? userId,
                $"Workflow action: {actionType}" + (string.IsNullOrEmpty(comments) ? "" : $" - {comments}")
            );

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing workflow action {ActionType} for form request {FormRequestId}", actionType, formRequestId);
            throw;
        }
    }

    public async Task<WorkflowStepInstance?> GetCurrentWorkflowStepAsync(int formRequestId)
    {
        try
        {
            _logger.LogInformation("GetCurrentWorkflowStepAsync called for formRequestId: {FormRequestId}", formRequestId);
            
            var formRequest = await GetFormRequestAsync(formRequestId);
            if (formRequest?.WorkflowInstanceId == null)
            {
                _logger.LogWarning("Form request {FormRequestId} has no WorkflowInstanceId", formRequestId);
                return null;
            }

            _logger.LogInformation("Form request {FormRequestId} has WorkflowInstanceId: {WorkflowInstanceId}", 
                formRequestId, formRequest.WorkflowInstanceId);

            var result = await _workflowService.GetCurrentWorkflowStepAsync(formRequest.WorkflowInstanceId.Value);
            
            _logger.LogInformation("GetCurrentWorkflowStepAsync result for formRequestId {FormRequestId}: {StepId}", 
                formRequestId, result?.StepId ?? "null");
                
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current workflow step for form request {FormRequestId}", formRequestId);
            throw;
        }
    }

    public async Task<List<WorkflowStepInstance>> GetCompletedWorkflowStepsAsync(int formRequestId)
    {
        try
        {
            var formRequest = await GetFormRequestAsync(formRequestId);
            if (formRequest?.WorkflowInstanceId == null)
            {
                return new List<WorkflowStepInstance>();
            }

            return await _workflowService.GetCompletedWorkflowStepsAsync(formRequest.WorkflowInstanceId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting completed workflow steps for form request {FormRequestId}", formRequestId);
            throw;
        }
    }
}
