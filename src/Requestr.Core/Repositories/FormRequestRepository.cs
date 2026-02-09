using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories.Queries;
using System.Text.Json;

namespace Requestr.Core.Repositories;

/// <summary>
/// Repository implementation for FormRequest data access.
/// </summary>
public class FormRequestRepository : IFormRequestRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FormRequestRepository> _logger;
    
    public FormRequestRepository(IDbConnectionFactory connectionFactory, ILogger<FormRequestRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }
    
    #region Read Operations
    
    public async Task<FormRequest?> GetByIdAsync(int id)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var row = await connection.QueryFirstOrDefaultAsync(
                FormRequestQueries.GetById, 
                new { Id = id },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return row == null ? null : MapFormRequest(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form request {Id}", id);
            throw;
        }
    }
    
    public async Task<FormRequest?> GetByIdWithFormDefinitionAsync(int id)
    {
        // This will be filled by calling the form definition service separately
        // The repository focuses on the FormRequest entity only
        return await GetByIdAsync(id);
    }
    
    public async Task<List<FormRequest>> GetAllAsync()
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestQueries.GetAll,
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return rows.Select(MapFormRequest).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all form requests");
            throw;
        }
    }
    
    public async Task<List<FormRequest>> GetByUserAsync(string userId)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestQueries.GetByUser, 
                new { UserId = userId },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return rows.Select(MapFormRequest).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for user {UserId}", userId);
            throw;
        }
    }
    
    public async Task<List<FormRequest>> GetByStatusAsync(RequestStatus status)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestQueries.GetByStatus, 
                new { Status = (int)status },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return rows.Select(MapFormRequest).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests by status {Status}", status);
            throw;
        }
    }
    
    public async Task<List<FormRequest>> GetByFormDefinitionAsync(int formDefinitionId)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestQueries.GetByFormDefinition, 
                new { FormDefinitionId = formDefinitionId },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return rows.Select(MapFormRequest).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for form definition {FormDefinitionId}", formDefinitionId);
            throw;
        }
    }
    
    public async Task<List<FormRequest>> GetPendingByFormDefinitionAsync(int formDefinitionId)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestQueries.GetPendingByFormDefinition, 
                new { 
                    FormDefinitionId = formDefinitionId,
                    Pending = (int)RequestStatus.Pending,
                    Approved = (int)RequestStatus.Approved
                },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return rows.Select(MapFormRequestSimple).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending form requests for form definition {FormDefinitionId}", formDefinitionId);
            throw;
        }
    }
    
    public async Task<List<FormRequest>> GetForApprovalAsync(List<string> approverRoles)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestQueries.GetForApproval, 
                new { Status = (int)RequestStatus.Pending },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            var result = new List<FormRequest>();
            foreach (var row in rows)
            {
                var formApproverRoles = JsonSerializer.Deserialize<List<string>>((string)(row.ApproverRoles ?? "[]")) ?? new List<string>();
                if (formApproverRoles.Any(role => approverRoles.Contains(role)))
                {
                    result.Add(MapFormRequest(row));
                }
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for approval");
            throw;
        }
    }
    
    public async Task<List<FormRequest>> GetAccessibleByUserAsync(string userId, List<string> userRoles)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestQueries.GetAccessibleByUser, 
                new { 
                    UserId = userId,
                    UserRolesJson = JsonSerializer.Serialize(userRoles),
                    ApprovalStepType = (int)WorkflowStepType.Approval
                },
                commandTimeout: _connectionFactory.LongRunningCommandTimeout);
            
            return rows.Select(MapFormRequest).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting accessible form requests for user {UserId}", userId);
            throw;
        }
    }
    
    public async Task<List<FormRequest>> GetWithCompletedWorkflowsNotAppliedAsync()
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rows = await connection.QueryAsync(
                FormRequestQueries.GetWithCompletedWorkflowsNotApplied, 
                new { 
                    WorkflowCompletedStatus = (int)WorkflowInstanceStatus.Completed,
                    ApprovedStatus = (int)RequestStatus.Approved
                },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return rows.Select(MapFormRequest).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests with completed workflows not applied");
            throw;
        }
    }
    
    public async Task<List<int>> GetApprovedNotAppliedIdsAsync()
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var ids = await connection.QueryAsync<int>(
                FormRequestQueries.GetApprovedNotAppliedIds, 
                new { Status = (int)RequestStatus.Approved },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            return ids.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting approved but not applied request IDs");
            throw;
        }
    }
    
    #endregion
    
    #region Write Operations
    
    public async Task<int> CreateAsync(FormRequest request)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var id = await connection.QuerySingleAsync<int>(
                FormRequestQueries.Create,
                new
                {
                    request.FormDefinitionId,
                    RequestType = (int)request.RequestType,
                    FieldValues = JsonSerializer.Serialize(request.FieldValues),
                    OriginalValues = JsonSerializer.Serialize(request.OriginalValues),
                    Status = (int)request.Status,
                    request.RequestedBy,
                    request.RequestedAt,
                    request.Comments,
                    request.AppliedRecordKey,
                    request.FailureMessage,
                    WorkflowInstanceId = (int?)null
                },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Created form request {Id} for form {FormDefinitionId}", id, request.FormDefinitionId);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating form request for form {FormDefinitionId}", request.FormDefinitionId);
            throw;
        }
    }
    
    public async Task<FormRequest> CreateAsync(FormRequest request, IDbConnection connection, IDbTransaction transaction)
    {
        var id = await connection.QuerySingleAsync<int>(
            FormRequestQueries.Create,
            new
            {
                request.FormDefinitionId,
                RequestType = (int)request.RequestType,
                FieldValues = JsonSerializer.Serialize(request.FieldValues),
                OriginalValues = JsonSerializer.Serialize(request.OriginalValues),
                Status = (int)request.Status,
                request.RequestedBy,
                request.RequestedAt,
                request.Comments,
                request.AppliedRecordKey,
                request.FailureMessage,
                WorkflowInstanceId = (int?)null
            },
            transaction,
            commandTimeout: _connectionFactory.BulkOperationCommandTimeout);
        
        request.Id = id;
        _logger.LogInformation("Created form request {Id} for form {FormDefinitionId} within transaction", id, request.FormDefinitionId);
        return request;
    }
    
    public async Task<FormRequest> UpdateAsync(FormRequest request)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            await connection.ExecuteAsync(
                FormRequestQueries.Update,
                new
                {
                    request.Id,
                    FieldValues = JsonSerializer.Serialize(request.FieldValues),
                    OriginalValues = JsonSerializer.Serialize(request.OriginalValues),
                    Status = (int)request.Status,
                    request.Comments
                },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Updated form request {Id}", request.Id);
            return request;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating form request {Id}", request.Id);
            throw;
        }
    }
    
    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            var rowsAffected = await connection.ExecuteAsync(
                "DELETE FROM FormRequests WHERE Id = @Id",
                new { Id = id },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Deleted form request {Id}, rows affected: {RowsAffected}", id, rowsAffected);
            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting form request {Id}", id);
            throw;
        }
    }
    
    public async Task UpdateStatusAsync(int id, RequestStatus status)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            await connection.ExecuteAsync(
                FormRequestQueries.UpdateStatus,
                new { Id = id, Status = (int)status },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Updated form request {Id} status to {Status}", id, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating form request {Id} status", id);
            throw;
        }
    }
    
    public async Task UpdateToApprovedAsync(int id, string approvedBy, string approvedByName, DateTime approvedAt)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            await connection.ExecuteAsync(
                FormRequestQueries.UpdateToApproved,
                new { Id = id, Status = (int)RequestStatus.Approved, ApprovedBy = approvedBy, ApprovedAt = approvedAt },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Approved form request {Id} by {ApprovedBy}", id, approvedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving form request {Id}", id);
            throw;
        }
    }
    
    public async Task UpdateToRejectedAsync(int id, string rejectedBy, string rejectedByName, string reason)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            await connection.ExecuteAsync(
                FormRequestQueries.UpdateToRejected,
                new { Id = id, Status = (int)RequestStatus.Rejected, RejectedBy = rejectedBy, RejectedAt = DateTime.UtcNow, RejectionReason = reason },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Rejected form request {Id} by {RejectedBy}", id, rejectedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting form request {Id}", id);
            throw;
        }
    }
    
    public async Task UpdateToAppliedAsync(int id, string? appliedRecordKey)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            await connection.ExecuteAsync(
                FormRequestQueries.UpdateToApplied,
                new { Id = id, Status = (int)RequestStatus.Applied, AppliedRecordKey = appliedRecordKey },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Applied form request {Id} with record key {RecordKey}", id, appliedRecordKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking form request {Id} as applied", id);
            throw;
        }
    }
    
    public async Task UpdateToFailedAsync(int id, string failureMessage)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            await connection.ExecuteAsync(
                FormRequestQueries.UpdateToFailed,
                new { Id = id, Status = (int)RequestStatus.Failed, FailureMessage = failureMessage },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Marked form request {Id} as failed: {Message}", id, failureMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking form request {Id} as failed", id);
            throw;
        }
    }
    
    public async Task UpdateWorkflowInstanceIdAsync(int id, int workflowInstanceId, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            FormRequestQueries.UpdateWorkflowInstanceId,
            new { Id = id, WorkflowInstanceId = workflowInstanceId },
            transaction,
            commandTimeout: _connectionFactory.BulkOperationCommandTimeout);
        
        _logger.LogInformation("Updated form request {Id} with workflow instance {WorkflowInstanceId}", id, workflowInstanceId);
    }
    
    public async Task ApproveAsync(int id, string approvedBy, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            FormRequestQueries.UpdateToApproved,
            new { Id = id, Status = (int)RequestStatus.Approved, ApprovedBy = approvedBy, ApprovedAt = DateTime.UtcNow },
            transaction,
            commandTimeout: _connectionFactory.DefaultCommandTimeout);
        
        _logger.LogInformation("Approved form request {Id} by {ApprovedBy} within transaction", id, approvedBy);
    }
    
    public async Task<int> RejectAsync(int id, string rejectedBy, string reason, IDbConnection connection, IDbTransaction? transaction)
    {
        var rowsAffected = await connection.ExecuteAsync(
            FormRequestQueries.UpdateToRejected,
            new { Id = id, Status = (int)RequestStatus.Rejected, RejectedBy = rejectedBy, RejectedAt = DateTime.UtcNow, RejectionReason = reason },
            transaction,
            commandTimeout: _connectionFactory.DefaultCommandTimeout);
        
        _logger.LogInformation("Rejected form request {Id} by {RejectedBy}", id, rejectedBy);
        return rowsAffected;
    }
    
    public async Task SetAppliedAsync(int id, string? recordKey, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            FormRequestQueries.UpdateToApplied,
            new { Id = id, Status = (int)RequestStatus.Applied, AppliedRecordKey = recordKey },
            transaction,
            commandTimeout: _connectionFactory.DefaultCommandTimeout);
        
        _logger.LogInformation("Set form request {Id} as applied with record key {RecordKey} within transaction", id, recordKey);
    }
    
    public async Task SetAppliedRecordKeyAsync(int id, string? recordKey)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            await connection.ExecuteAsync(
                "UPDATE FormRequests SET AppliedRecordKey = @AppliedRecordKey WHERE Id = @Id",
                new { Id = id, AppliedRecordKey = recordKey },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Set form request {Id} applied record key to {RecordKey}", id, recordKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting applied record key for form request {Id}", id);
            throw;
        }
    }
    
    public async Task SetFailedAsync(int id, string errorMessage, IDbConnection connection, IDbTransaction transaction)
    {
        await connection.ExecuteAsync(
            FormRequestQueries.UpdateToFailed,
            new { Id = id, Status = (int)RequestStatus.Failed, FailureMessage = errorMessage },
            transaction,
            commandTimeout: _connectionFactory.DefaultCommandTimeout);
        
        _logger.LogInformation("Set form request {Id} as failed within transaction: {Message}", id, errorMessage);
    }
    
    public async Task UpdateStatusAsync(int id, RequestStatus status, string? failureMessage)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await ((Microsoft.Data.SqlClient.SqlConnection)connection).OpenAsync();
            
            await connection.ExecuteAsync(
                "UPDATE FormRequests SET Status = @Status, FailureMessage = @FailureMessage WHERE Id = @Id",
                new { Id = id, Status = (int)status, FailureMessage = failureMessage },
                commandTimeout: _connectionFactory.DefaultCommandTimeout);
            
            _logger.LogInformation("Updated form request {Id} status to {Status}", id, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating form request {Id} status", id);
            throw;
        }
    }
    
    #endregion
    
    #region Mapping
    
    private FormRequest MapFormRequest(dynamic row)
    {
        return new FormRequest
        {
            Id = (int)row.Id,
            FormDefinitionId = (int)row.FormDefinitionId,
            RequestType = (RequestType)(int)row.RequestType,
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
            BulkFormRequestId = row.BulkFormRequestId != null ? (int?)row.BulkFormRequestId : null,
            FormDefinition = new FormDefinition
            {
                Name = (string)row.FormName,
                Description = (string)row.FormDescription,
                DatabaseConnectionName = (string)(row.FormDatabaseConnectionName ?? string.Empty),
                TableName = (string)(row.FormTableName ?? string.Empty),
                Schema = (string)(row.FormSchema ?? "dbo")
            },
            FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.FieldValuesJson ?? "{}")) ?? new Dictionary<string, object?>(),
            OriginalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.OriginalValuesJson ?? "{}")) ?? new Dictionary<string, object?>()
        };
    }
    
    private FormRequest MapFormRequestSimple(dynamic row)
    {
        var request = new FormRequest
        {
            Id = (int)row.Id,
            FormDefinitionId = (int)row.FormDefinitionId,
            RequestType = (RequestType)(int)row.RequestType,
            Status = (RequestStatus)(int)row.Status,
            RequestedBy = (string)row.RequestedBy,
            RequestedByName = (string)row.RequestedBy, // Fallback
            RequestedAt = (DateTime)row.RequestedAt,
            ApprovedBy = row.ApprovedBy as string,
            ApprovedByName = row.ApprovedBy as string, // Fallback
            ApprovedAt = row.ApprovedAt as DateTime?,
            RejectionReason = row.RejectionReason as string,
            Comments = row.Comments as string,
            AppliedRecordKey = row.AppliedRecordKey as string,
            FailureMessage = row.FailureMessage as string
        };
        
        var fieldValuesJson = row.FieldValuesJson as string;
        if (!string.IsNullOrEmpty(fieldValuesJson))
        {
            request.FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldValuesJson) ?? new Dictionary<string, object?>();
        }
        
        var originalValuesJson = row.OriginalValuesJson as string;
        if (!string.IsNullOrEmpty(originalValuesJson))
        {
            request.OriginalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(originalValuesJson) ?? new Dictionary<string, object?>();
        }
        
        return request;
    }
    
    #endregion
}
