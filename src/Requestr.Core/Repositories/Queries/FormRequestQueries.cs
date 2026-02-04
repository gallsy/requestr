namespace Requestr.Core.Repositories.Queries;

/// <summary>
/// SQL queries for FormRequest operations.
/// Centralizes all SQL to improve maintainability and consistency.
/// </summary>
public static class FormRequestQueries
{
    /// <summary>
    /// Base SELECT columns for form request queries.
    /// </summary>
    private const string BaseSelectColumns = @"
        fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
        fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy,
        COALESCE(uReq.DisplayName, fr.RequestedBy) as RequestedByName,
        fr.RequestedAt, fr.ApprovedBy, COALESCE(uApp.DisplayName, fr.ApprovedBy) as ApprovedByName,
        fr.ApprovedAt, fr.RejectionReason, fr.Comments,
        fr.AppliedRecordKey, fr.FailureMessage, fr.WorkflowInstanceId, fr.BulkFormRequestId,
        fd.Name as FormName, fd.Description as FormDescription";
    
    /// <summary>
    /// Base FROM clause with standard joins.
    /// </summary>
    private const string BaseFromClause = @"
        FROM FormRequests fr
        INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
        LEFT JOIN Users uReq ON uReq.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.RequestedBy)
        LEFT JOIN Users uApp ON uApp.UserObjectId = TRY_CONVERT(uniqueidentifier, fr.ApprovedBy)";
    
    /// <summary>
    /// Gets a form request by ID.
    /// </summary>
    public const string GetById = $@"
        SELECT {BaseSelectColumns}
        {BaseFromClause}
        WHERE fr.Id = @Id";
    
    /// <summary>
    /// Gets all form requests ordered by requested date descending.
    /// </summary>
    public const string GetAll = $@"
        SELECT {BaseSelectColumns}
        {BaseFromClause}
        ORDER BY fr.RequestedAt DESC";
    
    /// <summary>
    /// Gets form requests by user ID.
    /// </summary>
    public const string GetByUser = $@"
        SELECT {BaseSelectColumns}
        {BaseFromClause}
        WHERE fr.RequestedBy = @UserId
        ORDER BY fr.RequestedAt DESC";
    
    /// <summary>
    /// Gets form requests by status.
    /// </summary>
    public const string GetByStatus = $@"
        SELECT {BaseSelectColumns}
        {BaseFromClause}
        WHERE fr.Status = @Status
        ORDER BY fr.RequestedAt DESC";
    
    /// <summary>
    /// Gets form requests by form definition ID.
    /// </summary>
    public const string GetByFormDefinition = $@"
        SELECT {BaseSelectColumns}
        {BaseFromClause}
        WHERE fr.FormDefinitionId = @FormDefinitionId
        ORDER BY fr.RequestedAt DESC";
    
    /// <summary>
    /// Gets pending form requests by form definition ID.
    /// </summary>
    public const string GetPendingByFormDefinition = @"
        SELECT fr.Id, fr.FormDefinitionId, fr.RequestType, fr.FieldValues as FieldValuesJson, 
               fr.OriginalValues as OriginalValuesJson, fr.Status, fr.RequestedBy, fr.RequestedAt, 
               fr.ApprovedBy, fr.ApprovedAt, fr.RejectionReason,
               fr.Comments, fr.AppliedRecordKey, fr.FailureMessage
        FROM FormRequests fr
        WHERE fr.FormDefinitionId = @FormDefinitionId
          AND fr.Status IN (@Pending, @Approved)
        ORDER BY fr.RequestedAt DESC";
    
    /// <summary>
    /// Gets form requests for approval based on approver roles (legacy flow).
    /// </summary>
    public const string GetForApproval = $@"
        SELECT {BaseSelectColumns}, fd.ApproverRoles
        {BaseFromClause}
        WHERE fr.Status = @Status
        ORDER BY fr.RequestedAt DESC";
    
    /// <summary>
    /// Gets form requests with completed workflows that haven't been applied.
    /// </summary>
    public const string GetWithCompletedWorkflowsNotApplied = $@"
        SELECT {BaseSelectColumns},
               wi.Status as WorkflowStatus, wi.CompletedAt as WorkflowCompletedAt
        FROM FormRequests fr
        INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
        INNER JOIN WorkflowInstances wi ON fr.WorkflowInstanceId = wi.Id
        LEFT JOIN Users uReq ON TRY_CONVERT(uniqueidentifier, fr.RequestedBy) = uReq.UserObjectId
        LEFT JOIN Users uApp ON TRY_CONVERT(uniqueidentifier, fr.ApprovedBy) = uApp.UserObjectId
        WHERE wi.Status = @WorkflowCompletedStatus
            AND fr.Status = @ApprovedStatus
        ORDER BY fr.RequestedAt DESC";
    
    /// <summary>
    /// Gets IDs of approved but not applied requests.
    /// </summary>
    public const string GetApprovedNotAppliedIds = @"
        SELECT fr.Id
        FROM FormRequests fr
        WHERE fr.Status = @Status";
    
    /// <summary>
    /// Gets form requests accessible to a user (created by them or assignable to their roles).
    /// </summary>
    public const string GetAccessibleByUser = $@"
        SELECT {BaseSelectColumns}, fd.ApproverRoles
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
    
    /// <summary>
    /// Creates a new form request.
    /// </summary>
    public const string Create = @"
        INSERT INTO FormRequests (FormDefinitionId, RequestType, FieldValues, OriginalValues, Status, RequestedBy, RequestedAt, Comments, AppliedRecordKey, FailureMessage, WorkflowInstanceId)
        OUTPUT INSERTED.Id
        VALUES (@FormDefinitionId, @RequestType, @FieldValues, @OriginalValues, @Status, @RequestedBy, @RequestedAt, @Comments, @AppliedRecordKey, @FailureMessage, @WorkflowInstanceId)";
    
    /// <summary>
    /// Updates form request status.
    /// </summary>
    public const string UpdateStatus = @"
        UPDATE FormRequests 
        SET Status = @Status
        WHERE Id = @Id";
    
    /// <summary>
    /// Updates form request to approved status.
    /// </summary>
    public const string UpdateToApproved = @"
        UPDATE FormRequests 
        SET Status = @Status, ApprovedBy = @ApprovedBy, ApprovedAt = @ApprovedAt
        WHERE Id = @Id";
    
    /// <summary>
    /// Updates form request to rejected status.
    /// </summary>
    public const string UpdateToRejected = @"
        UPDATE FormRequests 
        SET Status = @Status, ApprovedBy = @RejectedBy, ApprovedAt = @RejectedAt, RejectionReason = @RejectionReason
        WHERE Id = @Id";
    
    /// <summary>
    /// Updates form request to applied status.
    /// </summary>
    public const string UpdateToApplied = @"
        UPDATE FormRequests 
        SET Status = @Status, AppliedRecordKey = @AppliedRecordKey, FailureMessage = NULL
        WHERE Id = @Id";
    
    /// <summary>
    /// Updates form request to failed status.
    /// </summary>
    public const string UpdateToFailed = @"
        UPDATE FormRequests 
        SET Status = @Status, FailureMessage = @FailureMessage
        WHERE Id = @Id";
    
    /// <summary>
    /// Updates workflow instance ID.
    /// </summary>
    public const string UpdateWorkflowInstanceId = @"
        UPDATE FormRequests 
        SET WorkflowInstanceId = @WorkflowInstanceId 
        WHERE Id = @Id";
    
    /// <summary>
    /// Full update of a form request.
    /// </summary>
    public const string Update = @"
        UPDATE FormRequests 
        SET FieldValues = @FieldValues, OriginalValues = @OriginalValues, 
            Status = @Status, Comments = @Comments
        WHERE Id = @Id";
}
