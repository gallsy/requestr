using Dapper;
using Microsoft.Extensions.Logging;
using Requestr.Core.Models;
using Requestr.Core.Repositories;
using System.Text.Json;

namespace Requestr.Core.Services.FormRequests;

/// <summary>
/// Implementation of form request query operations.
/// </summary>
public class FormRequestQueryService : IFormRequestQueryService
{
    private readonly IFormRequestRepository _formRequestRepository;
    private readonly IFormRequestHistoryService _historyService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FormRequestQueryService> _logger;

    public FormRequestQueryService(
        IFormRequestRepository formRequestRepository,
        IFormRequestHistoryService historyService,
        IDbConnectionFactory connectionFactory,
        ILogger<FormRequestQueryService> logger)
    {
        _formRequestRepository = formRequestRepository;
        _historyService = historyService;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<FormRequest?> GetByIdAsync(int id)
    {
        try
        {
            return await _formRequestRepository.GetByIdAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form request {Id}", id);
            throw;
        }
    }

    public async Task<List<FormRequest>> GetAllAsync()
    {
        try
        {
            return await _formRequestRepository.GetAllAsync();
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
            return await _formRequestRepository.GetByUserAsync(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for user {UserId}", userId);
            throw;
        }
    }

    public async Task<List<FormRequest>> GetByFormDefinitionAsync(int formDefinitionId)
    {
        try
        {
            return await _formRequestRepository.GetByFormDefinitionAsync(formDefinitionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for form definition {FormDefinitionId}", formDefinitionId);
            throw;
        }
    }

    public async Task<List<FormRequest>> GetByStatusAsync(RequestStatus status)
    {
        try
        {
            return await _formRequestRepository.GetByStatusAsync(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests with status {Status}", status);
            throw;
        }
    }

    public async Task<List<FormRequest>> GetPendingForApprovalAsync(List<string> approverRoles)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();
            
            var sql = @"
                SELECT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                    fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy,
                    COALESCE(uReq.DisplayName, fr.RequestedBy) as RequestedByName,
                    fr.RequestedAt, fr.ApprovedBy, COALESCE(uApp.DisplayName, fr.ApprovedBy) as ApprovedByName,
                    fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                    fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId, fr.BulkFormRequestId,
                    fd.Name as FormName, fd.Description as FormDescription, fd.ApproverRoles
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                LEFT JOIN Users uReq ON uReq.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
                LEFT JOIN Users uApp ON uApp.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.ApprovedBy)
                WHERE fr.Status = @Status
                  AND fr.BulkFormRequestId IS NULL
                ORDER BY fr.RequestedAt DESC";

            var requests = await connection.QueryAsync(sql, new { Status = (int)RequestStatus.Pending });
            var result = new List<FormRequest>();

            foreach (var row in requests)
            {
                var formApproverRoles = JsonSerializer.Deserialize<List<string>>((string)(row.ApproverRoles ?? "[]")) ?? new List<string>();
                if (formApproverRoles.Any(role => approverRoles.Contains(role)))
                {
                    var request = MapToFormRequest(row);
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

    public async Task<List<FormRequest>> GetForWorkflowApprovalAsync(string userId, List<string> userRoles)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            var sql = @"
                SELECT DISTINCT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                    fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, 
                    COALESCE(uReq.DisplayName, fr.RequestedBy) as RequestedByName, 
                    fr.RequestedAt, fr.ApprovedBy, COALESCE(uApp.DisplayName, fr.ApprovedBy) as ApprovedByName, 
                    fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                    fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId, fr.BulkFormRequestId,
                    fd.Name as FormName, fd.Description as FormDescription
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                INNER JOIN WorkflowInstances wi ON fr.WorkflowInstanceId = wi.Id
                INNER JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId
                INNER JOIN WorkflowSteps ws ON wsi.StepId = ws.StepId AND wi.WorkflowDefinitionId = ws.WorkflowDefinitionId
                LEFT JOIN Users uReq ON uReq.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
                LEFT JOIN Users uApp ON uApp.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.ApprovedBy)
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

            var isAdmin = userRoles.Contains("Admin");
            var parameters = new DynamicParameters();
            parameters.Add("ApprovalStepType", (int)WorkflowStepType.Approval);
            parameters.Add("PendingStatus", (int)WorkflowStepInstanceStatus.Pending);
            parameters.Add("InProgressStatus", (int)WorkflowStepInstanceStatus.InProgress);
            parameters.Add("CompletedStatus", (int)WorkflowStepInstanceStatus.Completed);
            parameters.Add("UserRoles", userRoles);
            parameters.Add("IsAdmin", isAdmin ? 1 : 0);

            var requests = await connection.QueryAsync(sql, parameters);
            var result = new List<FormRequest>();

            foreach (var row in requests)
            {
                var request = MapToFormRequest(row);
                request.WorkflowInstanceId = (int?)((IDictionary<string, object>)row)["WorkflowInstanceId"];
                result.Add(request);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form requests for workflow approval for user {UserId} with roles {Roles}", 
                userId, string.Join(", ", userRoles));
            throw;
        }
    }

    public async Task<List<FormRequest>> GetWithCompletedWorkflowsNotAppliedAsync()
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            var sql = @"
                SELECT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                    fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, 
                    COALESCE(uReq.DisplayName, fr.RequestedBy) AS RequestedByName, 
                    fr.RequestedAt, fr.ApprovedBy, COALESCE(uApp.DisplayName, fr.ApprovedBy) AS ApprovedByName, 
                    fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                    fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId, fr.BulkFormRequestId,
                    fd.Name as FormName, fd.Description as FormDescription
                FROM FormRequests fr
                INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                INNER JOIN WorkflowInstances wi ON fr.WorkflowInstanceId = wi.Id
                LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, fr.RequestedBy) = uReq.UserObjectId
                LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, fr.ApprovedBy) = uApp.UserObjectId
                WHERE wi.Status = @WorkflowCompletedStatus
                  AND fr.Status = @ApprovedStatus
                  AND fr.BulkFormRequestId IS NULL
                ORDER BY fr.RequestedAt DESC";

            var requests = await connection.QueryAsync(sql, new 
            { 
                WorkflowCompletedStatus = (int)WorkflowInstanceStatus.Completed,
                ApprovedStatus = (int)RequestStatus.Approved
            });

            var result = new List<FormRequest>();
            foreach (var row in requests)
            {
                result.Add(MapToFormRequest(row));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting requests with completed workflows but not applied");
            throw;
        }
    }

    public async Task<List<int>> GetApprovedNotAppliedIdsAsync()
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            const string sql = @"
                SELECT Id 
                FROM FormRequests 
                WHERE Status = @Status 
                ORDER BY Id";

            var requestIds = await connection.QueryAsync<int>(sql, new { Status = (int)RequestStatus.Approved });
            return requestIds.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting approved but not applied request IDs");
            throw;
        }
    }

    public async Task<List<FormRequest>> GetAccessibleFormRequestsAsync(string userId, List<string> userRoles)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            var parameters = new DynamicParameters();
            parameters.Add("UserId", userId);

            var isAdmin = userRoles.Contains("Admin");
            
            string sql;
            
            if (isAdmin)
            {
                sql = @"
                    SELECT DISTINCT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                        fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, 
                        COALESCE(uReq.DisplayName, fr.RequestedBy) AS RequestedByName, 
                        fr.RequestedAt, fr.ApprovedBy, COALESCE(uApp.DisplayName, fr.ApprovedBy) AS ApprovedByName, fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                        fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId, fr.BulkFormRequestId,
                        fd.Name as FormName, fd.Description as FormDescription, fd.ApproverRoles
                    FROM FormRequests fr
                    INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                    LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, fr.RequestedBy) = uReq.UserObjectId
                    LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, fr.ApprovedBy) = uApp.UserObjectId
                    ORDER BY fr.RequestedAt DESC";
            }
            else
            {
                sql = @"
                    SELECT DISTINCT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                           fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, 
                           COALESCE(uReq.DisplayName, fr.RequestedBy) AS RequestedByName, 
                           fr.RequestedAt, fr.ApprovedBy, COALESCE(uApp.DisplayName, fr.ApprovedBy) AS ApprovedByName, fr.ApprovedAt, fr.RejectionReason, fr.Comments,
                           fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId, fr.BulkFormRequestId,
                           fd.Name as FormName, fd.Description as FormDescription, fd.ApproverRoles
                    FROM FormRequests fr
                    INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
                    LEFT JOIN WorkflowInstances wi ON fr.WorkflowInstanceId = wi.Id
                    LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, fr.RequestedBy) = uReq.UserObjectId
                    LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, fr.ApprovedBy) = uApp.UserObjectId
                    WHERE 
                        fr.RequestedBy = @UserId
                        OR
                        (fr.WorkflowInstanceId IS NOT NULL AND EXISTS (
                            SELECT 1 FROM WorkflowSteps ws
                            WHERE ws.WorkflowDefinitionId = wi.WorkflowDefinitionId
                            AND ws.StepType = @ApprovalStepType
                            AND ws.AssignedRoles IS NOT NULL 
                            AND ws.AssignedRoles != '[]'
                            AND EXISTS (
                                SELECT 1 FROM OPENJSON(ws.AssignedRoles) AS roles
                                WHERE roles.value IN (SELECT value FROM OPENJSON(@UserRolesJson))
                            )
                        ))
                    ORDER BY fr.RequestedAt DESC";
                
                parameters.Add("UserRolesJson", JsonSerializer.Serialize(userRoles));
                parameters.Add("ApprovalStepType", (int)WorkflowStepType.Approval);
            }

            var requests = await connection.QueryAsync(sql, parameters);
            var result = new List<FormRequest>();

            foreach (var row in requests)
            {
                var request = MapToFormRequest(row);
                result.Add(request);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting accessible form requests for user {UserId}", userId);
            throw;
        }
    }

    public async Task<FormRequest?> GetWithHistoryAsync(int id)
    {
        try
        {
            var request = await _formRequestRepository.GetByIdAsync(id);
            if (request != null)
            {
                request.History = await _historyService.GetHistoryAsync(id);
            }
            return request;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting form request with history {Id}", id);
            throw;
        }
    }

    public async Task<List<FormRequest>> GetPendingByFormDefinitionAsync(int formDefinitionId)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();
            
            var sql = @"
                SELECT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
                       fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, 
                       COALESCE(uReq.DisplayName, fr.RequestedBy) AS RequestedByName,
                       fr.RequestedAt, fr.ApprovedBy, 
                       COALESCE(uApp.DisplayName, fr.ApprovedBy) AS ApprovedByName,
                       fr.ApprovedAt, fr.RejectionReason,
                       fr.Comments, fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId, fr.BulkFormRequestId
                FROM FormRequests fr
                LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, fr.RequestedBy) = uReq.UserObjectId
                LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, fr.ApprovedBy) = uApp.UserObjectId
                WHERE fr.FormDefinitionId = @FormDefinitionId
                  AND fr.Status IN (@Pending, @Approved)
                  AND fr.BulkFormRequestId IS NULL
                ORDER BY fr.RequestedAt DESC";
            
            var rows = await connection.QueryAsync(sql, new 
            { 
                FormDefinitionId = formDefinitionId,
                Pending = (int)RequestStatus.Pending,
                Approved = (int)RequestStatus.Approved
            });
            
            var requests = new List<FormRequest>();
            foreach (var row in rows)
            {
                var request = new FormRequest
                {
                    Id = (int)row.Id,
                    FormDefinitionId = (int)row.FormDefinitionId,
                    RequestType = (RequestType)(int)row.RequestType,
                    Status = (RequestStatus)(int)row.Status,
                    RequestedBy = (string)row.RequestedBy,
                    RequestedByName = (string)row.RequestedByName,
                    RequestedAt = (DateTime)row.RequestedAt,
                    ApprovedBy = row.ApprovedBy as string,
                    ApprovedByName = row.ApprovedByName as string,
                    ApprovedAt = row.ApprovedAt as DateTime?,
                    RejectionReason = row.RejectionReason as string,
                    Comments = row.Comments as string,
                    AppliedRecordKey = row.AppliedRecordKey as string,
                    FailureMessage = row.FailureMessage as string,
                    WorkflowInstanceId = row.WorkflowInstanceId as int?,
                    BulkFormRequestId = row.BulkFormRequestId as int?
                };
                
                var fieldValuesJson = row.FieldValuesJson as string;
                if (!string.IsNullOrEmpty(fieldValuesJson))
                {
                    request.FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldValuesJson) 
                        ?? new Dictionary<string, object?>();
                }
                
                var originalValuesJson = row.OriginalValuesJson as string;
                if (!string.IsNullOrEmpty(originalValuesJson))
                {
                    request.OriginalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>(originalValuesJson) 
                        ?? new Dictionary<string, object?>();
                }
                
                requests.Add(request);
            }
            
            _logger.LogInformation("Found {Count} pending requests for form definition {FormDefinitionId}", 
                requests.Count, formDefinitionId);
            
            return requests;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending requests for form definition {FormDefinitionId}", formDefinitionId);
            throw;
        }
    }

    private static FormRequest MapToFormRequest(dynamic row)
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
            BulkFormRequestId = (int?)row.BulkFormRequestId,
            FormDefinition = new FormDefinition { Name = (string)row.FormName, Description = (string)row.FormDescription },
            FieldValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.FieldValuesJson ?? "{}")) ?? new Dictionary<string, object?>(),
            OriginalValues = JsonSerializer.Deserialize<Dictionary<string, object?>>((string)(row.OriginalValuesJson ?? "{}")) ?? new Dictionary<string, object?>()
        };
    }
}
