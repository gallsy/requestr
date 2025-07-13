-- Workflow Performance Optimizations
-- This script adds additional indexes and performance improvements for workflow operations
-- Run this after the workflow-schema.sql

USE RequestrApp;
GO

PRINT 'Starting workflow performance optimizations...';

-- Add composite indexes for common query patterns
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowInstances_FormRequestId_Status' AND object_id = OBJECT_ID('WorkflowInstances'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkflowInstances_FormRequestId_Status 
    ON WorkflowInstances (FormRequestId, Status) 
    INCLUDE (Id, WorkflowDefinitionId, CurrentStepId, StartedAt, CompletedAt);
    PRINT 'Created index IX_WorkflowInstances_FormRequestId_Status';
END

-- Index for workflow step instances ordered queries
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowStepInstances_WorkflowInstanceId_StartedAt' AND object_id = OBJECT_ID('WorkflowStepInstances'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkflowStepInstances_WorkflowInstanceId_StartedAt 
    ON WorkflowStepInstances (WorkflowInstanceId, StartedAt) 
    INCLUDE (StepId, Status, AssignedTo, CompletedAt, CompletedBy, Action);
    PRINT 'Created index IX_WorkflowStepInstances_WorkflowInstanceId_StartedAt';
END

-- Index for finding pending workflow steps by user/role
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowStepInstances_Status_AssignedTo' AND object_id = OBJECT_ID('WorkflowStepInstances'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkflowStepInstances_Status_AssignedTo 
    ON WorkflowStepInstances (Status, AssignedTo) 
    INCLUDE (WorkflowInstanceId, StepId, StartedAt);
    PRINT 'Created index IX_WorkflowStepInstances_Status_AssignedTo';
END

-- Index for workflow definitions with form mapping
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowDefinitions_FormDefinitionId_IsActive' AND object_id = OBJECT_ID('WorkflowDefinitions'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkflowDefinitions_FormDefinitionId_IsActive 
    ON WorkflowDefinitions (FormDefinitionId, IsActive) 
    INCLUDE (Id, Name, Version, CreatedAt);
    PRINT 'Created index IX_WorkflowDefinitions_FormDefinitionId_IsActive';
END

-- Index for workflow steps lookup by workflow and step type
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowSteps_WorkflowDefinitionId_StepType' AND object_id = OBJECT_ID('WorkflowSteps'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkflowSteps_WorkflowDefinitionId_StepType 
    ON WorkflowSteps (WorkflowDefinitionId, StepType) 
    INCLUDE (StepId, Name, AssignedRoles, IsRequired);
    PRINT 'Created index IX_WorkflowSteps_WorkflowDefinitionId_StepType';
END

-- Update statistics for better query planning
UPDATE STATISTICS WorkflowInstances;
UPDATE STATISTICS WorkflowStepInstances;
UPDATE STATISTICS WorkflowDefinitions;
UPDATE STATISTICS WorkflowSteps;

PRINT 'Updated statistics for workflow tables';

-- Add a view for active workflow instances with current step info
IF OBJECT_ID('vw_ActiveWorkflowInstances', 'V') IS NOT NULL
    DROP VIEW vw_ActiveWorkflowInstances;
GO

CREATE VIEW vw_ActiveWorkflowInstances AS
SELECT 
    wi.Id as WorkflowInstanceId,
    wi.FormRequestId,
    wi.WorkflowDefinitionId,
    wi.CurrentStepId,
    wi.Status,
    wi.StartedAt,
    wd.Name as WorkflowName,
    wd.FormDefinitionId,
    fd.Name as FormName,
    fd.TableName,
    ws.Name as CurrentStepName,
    ws.StepType as CurrentStepType,
    ws.AssignedRoles as CurrentStepRoles,
    wsi.Status as CurrentStepStatus,
    wsi.AssignedTo as CurrentStepAssignedTo,
    wsi.StartedAt as CurrentStepStartedAt
FROM WorkflowInstances wi
    INNER JOIN WorkflowDefinitions wd ON wi.WorkflowDefinitionId = wd.Id
    INNER JOIN FormDefinitions fd ON wd.FormDefinitionId = fd.Id
    LEFT JOIN WorkflowSteps ws ON wd.Id = ws.WorkflowDefinitionId AND wi.CurrentStepId = ws.StepId
    LEFT JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId AND wi.CurrentStepId = wsi.StepId
WHERE wi.Status = 0; -- InProgress
GO

PRINT 'Created view vw_ActiveWorkflowInstances';

-- Add a function to get workflow progress percentage
IF OBJECT_ID('fn_GetWorkflowProgress', 'FN') IS NOT NULL
    DROP FUNCTION fn_GetWorkflowProgress;
GO

CREATE FUNCTION fn_GetWorkflowProgress(@WorkflowInstanceId INT)
RETURNS DECIMAL(5,2)
AS
BEGIN
    DECLARE @TotalSteps INT;
    DECLARE @CompletedSteps INT;
    DECLARE @Progress DECIMAL(5,2);
    
    SELECT @TotalSteps = COUNT(*)
    FROM WorkflowStepInstances 
    WHERE WorkflowInstanceId = @WorkflowInstanceId;
    
    SELECT @CompletedSteps = COUNT(*)
    FROM WorkflowStepInstances 
    WHERE WorkflowInstanceId = @WorkflowInstanceId 
      AND Status = 2; -- Completed
    
    IF @TotalSteps = 0 
        SET @Progress = 0;
    ELSE
        SET @Progress = CAST(@CompletedSteps AS DECIMAL(5,2)) / CAST(@TotalSteps AS DECIMAL(5,2)) * 100;
    
    RETURN @Progress;
END;
GO

PRINT 'Created function fn_GetWorkflowProgress';

-- Additional optimizations for timeout and blocking issues (Added 2025-07-13)

-- Add index for faster form request data retrieval during workflow completion
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FormRequests_Status_FormDefinitionId' AND object_id = OBJECT_ID('FormRequests'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_FormRequests_Status_FormDefinitionId 
    ON FormRequests (Status, FormDefinitionId) 
    INCLUDE (Id, RequestType, FieldValues, OriginalValues, WorkflowInstanceId);
    PRINT 'Created index IX_FormRequests_Status_FormDefinitionId';
END

-- Add index for FormDefinitions with database connection info
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_FormDefinitions_DatabaseConnection' AND object_id = OBJECT_ID('FormDefinitions'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_FormDefinitions_DatabaseConnection 
    ON FormDefinitions (DatabaseConnectionName) 
    INCLUDE (Id, TableName, [Schema], Name);
    PRINT 'Created index IX_FormDefinitions_DatabaseConnection';
END

-- Add view for monitoring stuck workflow requests
IF OBJECT_ID('vw_StuckWorkflowRequests', 'V') IS NOT NULL
    DROP VIEW vw_StuckWorkflowRequests;
GO

CREATE VIEW vw_StuckWorkflowRequests
AS
SELECT 
    fr.Id as FormRequestId,
    fr.FormDefinitionId,
    fd.Name as FormName,
    fr.RequestType,
    fr.Status as RequestStatus,
    fr.RequestedBy,
    fr.RequestedByName,
    fr.RequestedAt,
    fr.ApprovedAt,
    wi.Id as WorkflowInstanceId,
    wi.Status as WorkflowStatus,
    wi.CurrentStepId,
    wi.CompletedAt as WorkflowCompletedAt,
    wi.CompletedBy as WorkflowCompletedBy,
    DATEDIFF(MINUTE, wi.CompletedAt, GETUTCDATE()) as MinutesStuckSinceCompletion
FROM FormRequests fr
INNER JOIN FormDefinitions fd ON fr.FormDefinitionId = fd.Id
INNER JOIN WorkflowInstances wi ON fr.WorkflowInstanceId = wi.Id
WHERE wi.Status = 1 -- WorkflowInstanceStatus.Completed
  AND fr.Status = 1 -- RequestStatus.Approved (not Applied)
  AND wi.CompletedAt IS NOT NULL;
GO

PRINT 'Created view vw_StuckWorkflowRequests for monitoring stuck requests';

-- Add function to get workflow health statistics
IF OBJECT_ID('fn_GetWorkflowHealthStats', 'TF') IS NOT NULL
    DROP FUNCTION fn_GetWorkflowHealthStats;
GO

CREATE FUNCTION fn_GetWorkflowHealthStats()
RETURNS @WorkflowStats TABLE (
    Metric NVARCHAR(50),
    Count INT,
    Description NVARCHAR(255)
)
AS
BEGIN
    INSERT INTO @WorkflowStats (Metric, Count, Description)
    VALUES 
        ('TotalWorkflows', (SELECT COUNT(*) FROM WorkflowInstances), 'Total workflow instances'),
        ('InProgressWorkflows', (SELECT COUNT(*) FROM WorkflowInstances WHERE Status = 0), 'Workflows currently in progress'),
        ('CompletedWorkflows', (SELECT COUNT(*) FROM WorkflowInstances WHERE Status = 1), 'Successfully completed workflows'),
        ('RejectedWorkflows', (SELECT COUNT(*) FROM WorkflowInstances WHERE Status = 2), 'Rejected workflows'),
        ('FailedWorkflows', (SELECT COUNT(*) FROM WorkflowInstances WHERE Status = 4), 'Failed workflows'),
        ('StuckRequests', (SELECT COUNT(*) FROM vw_StuckWorkflowRequests), 'Requests with completed workflows but not applied'),
        ('PendingRequests', (SELECT COUNT(*) FROM FormRequests WHERE Status = 0), 'Requests pending approval'),
        ('ApprovedRequests', (SELECT COUNT(*) FROM FormRequests WHERE Status = 1), 'Approved but not applied requests'),
        ('AppliedRequests', (SELECT COUNT(*) FROM FormRequests WHERE Status = 3), 'Successfully applied requests'),
        ('FailedRequests', (SELECT COUNT(*) FROM FormRequests WHERE Status = 4), 'Failed requests');
    
    RETURN;
END
GO

PRINT 'Created function fn_GetWorkflowHealthStats for monitoring';

PRINT 'Workflow performance optimizations completed successfully!';
PRINT '';
PRINT 'Monitoring commands:';
PRINT '1. Check stuck requests: SELECT * FROM vw_StuckWorkflowRequests';
PRINT '2. Get health stats: SELECT * FROM fn_GetWorkflowHealthStats()';
PRINT '3. Use the admin Workflow Diagnostics page for processing stuck requests';
