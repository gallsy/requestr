namespace Requestr.Core.Repositories.Queries;

/// <summary>
/// SQL queries for Workflow operations.
/// </summary>
public static class WorkflowQueries
{
    #region Workflow Definition Queries
    
    /// <summary>
    /// Gets workflow definition with steps, transitions, and field configurations.
    /// </summary>
    public const string GetDefinitionById = @"
        SELECT * FROM WorkflowDefinitions WHERE Id = @Id;
        SELECT * FROM WorkflowSteps WHERE WorkflowDefinitionId = @Id;
        SELECT * FROM WorkflowTransitions WHERE WorkflowDefinitionId = @Id;
        SELECT wfc.* FROM WorkflowStepFieldConfigurations wfc
        INNER JOIN WorkflowSteps ws ON wfc.WorkflowStepId = ws.Id
        WHERE ws.WorkflowDefinitionId = @Id;";
    
    /// <summary>
    /// Gets workflow definition ID for a form.
    /// </summary>
    public const string GetDefinitionIdByFormId = @"
        SELECT fd.WorkflowDefinitionId
        FROM FormDefinitions fd
        WHERE fd.Id = @FormDefinitionId";
    
    /// <summary>
    /// Gets all workflow definitions.
    /// </summary>
    public const string GetAllDefinitions = @"
        SELECT * FROM WorkflowDefinitions ORDER BY Name";
    
    /// <summary>
    /// Creates a workflow definition.
    /// </summary>
    public const string CreateDefinition = @"
        INSERT INTO WorkflowDefinitions (FormDefinitionId, Name, Description, Version, CreatedBy, CreatedAt)
        OUTPUT INSERTED.Id
        VALUES (@FormDefinitionId, @Name, @Description, @Version, @CreatedBy, @CreatedAt)";
    
    /// <summary>
    /// Updates a workflow definition.
    /// </summary>
    public const string UpdateDefinition = @"
        UPDATE WorkflowDefinitions 
        SET Name = @Name, Description = @Description, Version = @Version, UpdatedBy = @UpdatedBy, UpdatedAt = @UpdatedAt
        WHERE Id = @Id";
    
    /// <summary>
    /// Deletes a workflow definition.
    /// </summary>
    public const string DeleteDefinition = @"
        DELETE FROM WorkflowDefinitions WHERE Id = @Id";
    
    #endregion
    
    #region Workflow Step Queries
    
    /// <summary>
    /// Creates a workflow step.
    /// </summary>
    public const string CreateStep = @"
        INSERT INTO WorkflowSteps (WorkflowDefinitionId, StepId, StepType, Name, Description, AssignedRoles, PositionX, PositionY, Configuration, IsRequired, NotificationEmail)
        OUTPUT INSERTED.Id
        VALUES (@WorkflowDefinitionId, @StepId, @StepType, @Name, @Description, @AssignedRoles, @PositionX, @PositionY, @Configuration, @IsRequired, @NotificationEmail)";
    
    /// <summary>
    /// Deletes steps for a workflow definition.
    /// </summary>
    public const string DeleteStepsByDefinitionId = @"
        DELETE FROM WorkflowStepFieldConfigurations WHERE WorkflowStepId IN (SELECT Id FROM WorkflowSteps WHERE WorkflowDefinitionId = @Id);
        DELETE FROM WorkflowSteps WHERE WorkflowDefinitionId = @Id";
    
    /// <summary>
    /// Gets field configurations for a workflow step by its database ID.
    /// </summary>
    public const string GetFieldConfigurationsByStepDbId = @"
        SELECT Id, WorkflowStepId, FieldName, IsRequired, IsReadOnly, IsVisible
        FROM WorkflowStepFieldConfigurations
        WHERE WorkflowStepId = @WorkflowStepId";
    
    #endregion
    
    #region Workflow Transition Queries
    
    /// <summary>
    /// Gets transitions from a step.
    /// </summary>
    public const string GetTransitionsFromStep = @"
        SELECT wt.* FROM WorkflowTransitions wt
        INNER JOIN WorkflowInstances wi ON wt.WorkflowDefinitionId = wi.WorkflowDefinitionId
        WHERE wi.Id = @WorkflowInstanceId AND wt.FromStepId = @CurrentStepId";
    
    /// <summary>
    /// Creates a workflow transition.
    /// </summary>
    public const string CreateTransition = @"
        INSERT INTO WorkflowTransitions (WorkflowDefinitionId, FromStepId, ToStepId, Name, Condition)
        OUTPUT INSERTED.Id
        VALUES (@WorkflowDefinitionId, @FromStepId, @ToStepId, @Name, @Condition)";
    
    /// <summary>
    /// Deletes transitions for a workflow definition.
    /// </summary>
    public const string DeleteTransitionsByDefinitionId = @"
        DELETE FROM WorkflowTransitions WHERE WorkflowDefinitionId = @Id";
    
    #endregion
    
    #region Workflow Instance Queries
    
    /// <summary>
    /// Gets workflow instance with step instances.
    /// </summary>
    public const string GetInstanceById = @"
        SELECT * FROM WorkflowInstances WHERE Id = @Id;
        SELECT * FROM WorkflowStepInstances WHERE WorkflowInstanceId = @Id ORDER BY ISNULL(StartedAt, '1900-01-01'), Id;";
    
    /// <summary>
    /// Gets workflow instance ID by form request ID.
    /// </summary>
    public const string GetInstanceIdByFormRequestId = @"
        SELECT Id FROM WorkflowInstances WHERE FormRequestId = @FormRequestId";
    
    /// <summary>
    /// Gets active workflow instances.
    /// </summary>
    public const string GetActiveInstances = @"
        SELECT * FROM WorkflowInstances WHERE Status = @Status ORDER BY StartedAt DESC";
    
    /// <summary>
    /// Gets workflow instances by user.
    /// </summary>
    public const string GetInstancesByUser = @"
        SELECT DISTINCT wi.* FROM WorkflowInstances wi
        INNER JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId
        WHERE wsi.AssignedTo = @UserId OR wi.CompletedBy = @UserId
        ORDER BY wi.StartedAt DESC";
    
    /// <summary>
    /// Creates a workflow instance.
    /// </summary>
    public const string CreateInstance = @"
        INSERT INTO WorkflowInstances (FormRequestId, WorkflowDefinitionId, CurrentStepId, Status, StartedAt)
        OUTPUT INSERTED.Id
        VALUES (@FormRequestId, @WorkflowDefinitionId, @CurrentStepId, @Status, @StartedAt)";
    
    /// <summary>
    /// Updates workflow instance current step.
    /// </summary>
    public const string UpdateInstanceCurrentStep = @"
        UPDATE WorkflowInstances SET CurrentStepId = @CurrentStepId WHERE Id = @Id";
    
    /// <summary>
    /// Updates workflow instance to completed.
    /// </summary>
    public const string UpdateInstanceToCompleted = @"
        UPDATE WorkflowInstances 
        SET Status = @Status, CompletedAt = @CompletedAt, CompletedBy = @CompletedBy 
        WHERE Id = @Id";
    
    #endregion
    
    #region Workflow Step Instance Queries
    
    /// <summary>
    /// Gets current step instance by workflow instance ID.
    /// </summary>
    public const string GetCurrentStepInstance = @"
        SELECT wsi.*, COALESCE(u.DisplayName, wsi.CompletedBy) AS CompletedByName
        FROM WorkflowStepInstances wsi
        INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
        LEFT JOIN Users u ON TRY_CONVERT(uniqueidentifier, wsi.CompletedBy) = u.UserObjectId
        WHERE wi.Id = @WorkflowInstanceId AND wsi.StepId = wi.CurrentStepId";
    
    /// <summary>
    /// Gets in-progress step instance.
    /// </summary>
    public const string GetInProgressStepInstance = @"
        SELECT wsi.*, COALESCE(u.DisplayName, wsi.CompletedBy) AS CompletedByName
        FROM WorkflowStepInstances wsi
        LEFT JOIN Users u ON TRY_CONVERT(uniqueidentifier, wsi.CompletedBy) = u.UserObjectId
        WHERE wsi.WorkflowInstanceId = @WorkflowInstanceId 
        AND wsi.Status = @InProgressStatus
        ORDER BY wsi.StartedAt DESC";
    
    /// <summary>
    /// Gets step instances by workflow instance ID.
    /// </summary>
    public const string GetStepInstancesByWorkflowId = @"
        SELECT wsi.*, COALESCE(u.DisplayName, wsi.CompletedBy) AS CompletedByName
        FROM WorkflowStepInstances wsi
        LEFT JOIN Users u ON TRY_CONVERT(uniqueidentifier, wsi.CompletedBy) = u.UserObjectId
        WHERE wsi.WorkflowInstanceId = @WorkflowInstanceId 
        ORDER BY ISNULL(wsi.StartedAt, '1900-01-01'), wsi.Id";
    
    /// <summary>
    /// Gets completed step instances.
    /// </summary>
    public const string GetCompletedStepInstances = @"
        SELECT wsi.*, COALESCE(u.DisplayName, wsi.CompletedBy) AS CompletedByName 
        FROM WorkflowStepInstances wsi
        LEFT JOIN Users u ON TRY_CONVERT(uniqueidentifier, wsi.CompletedBy) = u.UserObjectId
        WHERE wsi.WorkflowInstanceId = @WorkflowInstanceId 
        AND wsi.Status = @CompletedStatus
        ORDER BY wsi.CompletedAt ASC";
    
    /// <summary>
    /// Gets pending step instances for a user.
    /// </summary>
    public const string GetPendingStepsForUser = @"
        SELECT wsi.*, wi.FormRequestId, wi.WorkflowDefinitionId
        FROM WorkflowStepInstances wsi
        INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
        INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
        WHERE wsi.Status = @InProgress
        AND wi.Status = @WorkflowInProgress
        AND ws.StepType = @ApprovalStepType
        AND (
            wsi.AssignedTo = @UserId 
            OR wsi.AssignedTo IS NULL
            OR ws.AssignedRoles IS NULL 
            OR ws.AssignedRoles = '[]'
            OR ws.AssignedRoles = ''
        )
        ORDER BY ISNULL(wsi.StartedAt, '1900-01-01'), wsi.Id ASC";
    
    /// <summary>
    /// Creates a step instance.
    /// </summary>
    public const string CreateStepInstance = @"
        INSERT INTO WorkflowStepInstances (WorkflowInstanceId, StepId, Status, StartedAt, FieldValues)
        VALUES (@WorkflowInstanceId, @StepId, @Status, @StartedAt, @FieldValues)";
    
    /// <summary>
    /// Updates step instance to in-progress.
    /// </summary>
    public const string UpdateStepToInProgress = @"
        UPDATE WorkflowStepInstances 
        SET Status = @Status, StartedAt = @StartedAt 
        WHERE WorkflowInstanceId = @WorkflowInstanceId AND StepId = @StepId";
    
    /// <summary>
    /// Updates step instance to completed.
    /// </summary>
    public const string UpdateStepToCompleted = @"
        UPDATE WorkflowStepInstances 
        SET Status = @Status, CompletedAt = @CompletedAt, CompletedBy = @CompletedBy, 
            Action = @Action, Comments = @Comments, FieldValues = @FieldValues
        WHERE WorkflowInstanceId = @WorkflowInstanceId AND StepId = @StepId";
    
    /// <summary>
    /// Counts step instances for verification.
    /// </summary>
    public const string CountStepInstances = @"
        SELECT COUNT(*) FROM WorkflowStepInstances WHERE WorkflowInstanceId = @WorkflowInstanceId";
    
    /// <summary>
    /// Gets step instance assigned roles from step definition.
    /// </summary>
    public const string GetStepInstanceWithRoles = @"
        SELECT wsi.*, ws.AssignedRoles
        FROM WorkflowStepInstances wsi
        INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
        INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
        WHERE wsi.WorkflowInstanceId = @WorkflowInstanceId AND wsi.StepId = @StepId";

    #endregion

    #region Workflow Progress Queries

    /// <summary>
    /// Gets workflow progress data for a form request.
    /// </summary>
    public const string GetWorkflowProgress = @"
        SELECT 
            wi.Id as WorkflowInstanceId,
            wi.FormRequestId,
            wi.WorkflowDefinitionId,
            wi.Status,
            wi.CurrentStepId,
            wi.StartedAt,
            wi.CompletedAt,
            wi.CompletedBy,
            wd.Name as WorkflowName,
            csi.Status as CurrentStepStatus,
            csi.StartedAt as CurrentStepStartedAt,
            csi.AssignedTo as CurrentStepAssignedTo,
            cs.Name as CurrentStepName
        FROM WorkflowInstances wi
        INNER JOIN WorkflowDefinitions wd ON wi.WorkflowDefinitionId = wd.Id
        LEFT JOIN WorkflowStepInstances csi ON wi.Id = csi.WorkflowInstanceId AND wi.CurrentStepId = csi.StepId
        LEFT JOIN WorkflowSteps cs ON wd.Id = cs.WorkflowDefinitionId AND wi.CurrentStepId = cs.StepId
        WHERE wi.FormRequestId = @FormRequestId";

    /// <summary>
    /// Gets step progress data for workflow instance.
    /// </summary>
    public const string GetStepProgress = @"
        SELECT 
            wsi.StepId,
            ws.Name as StepName,
            ws.Description as StepDescription,
            ws.StepType,
            ws.AssignedRoles,
            wsi.Status,
            wsi.AssignedTo,
            wsi.StartedAt,
            wsi.CompletedAt,
            wsi.CompletedBy,
            COALESCE(u.DisplayName, wsi.CompletedBy) as CompletedByName,
            wsi.Action,
            wsi.Comments
        FROM WorkflowStepInstances wsi
        INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
        INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
        LEFT JOIN Users u ON TRY_CONVERT(uniqueidentifier, wsi.CompletedBy) = u.UserObjectId
        WHERE wsi.WorkflowInstanceId = @WorkflowInstanceId
        ORDER BY 
            CASE ws.StepType 
                WHEN 0 THEN 0 
                WHEN 4 THEN 999 
                ELSE 1 
            END,
            wsi.StartedAt";

    /// <summary>
    /// Gets step counts for workflow progress calculation.
    /// </summary>
    public const string GetStepCounts = @"
        SELECT 
            COUNT(*) as TotalSteps,
            SUM(CASE WHEN Status = @CompletedStatus THEN 1 ELSE 0 END) as CompletedSteps
        FROM WorkflowStepInstances 
        WHERE WorkflowInstanceId = @WorkflowInstanceId";

    #endregion

    #region Workflow History Queries

    /// <summary>
    /// Gets workflow history for a form request.
    /// </summary>
    public const string GetWorkflowHistory = @"
        SELECT 
            wsi.StepId,
            ws.Name as StepName,
            wsi.Status,
            wsi.Action,
            wsi.Comments,
            wsi.StartedAt,
            wsi.CompletedAt,
            wsi.CompletedBy,
            COALESCE(u.DisplayName, wsi.CompletedBy) as CompletedByName
        FROM WorkflowStepInstances wsi
        INNER JOIN WorkflowInstances wi ON wsi.WorkflowInstanceId = wi.Id
        INNER JOIN WorkflowSteps ws ON ws.WorkflowDefinitionId = wi.WorkflowDefinitionId AND ws.StepId = wsi.StepId
        LEFT JOIN Users u ON TRY_CONVERT(uniqueidentifier, wsi.CompletedBy) = u.UserObjectId
        WHERE wi.FormRequestId = @FormRequestId
        ORDER BY wsi.CompletedAt DESC, wsi.StartedAt DESC";

    #endregion
}
