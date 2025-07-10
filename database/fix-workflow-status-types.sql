-- Fix Status Data Type Issues in Workflow Tables
-- This script addresses string/int conversion issues with workflow status columns

USE RequestrApp;
GO

PRINT 'WORKFLOW STATUS DATA TYPE FIX - ' + CONVERT(varchar, GETDATE(), 120);
PRINT '';

-- 1. Check current status values in WorkflowInstances
PRINT '1. CURRENT WORKFLOW INSTANCE STATUS VALUES:';
SELECT DISTINCT Status, COUNT(*) as Count
FROM WorkflowInstances
GROUP BY Status
ORDER BY Status;

-- 2. Check current status values in WorkflowStepInstances
PRINT '';
PRINT '2. CURRENT WORKFLOW STEP INSTANCE STATUS VALUES:';
SELECT DISTINCT Status, COUNT(*) as Count
FROM WorkflowStepInstances
GROUP BY Status
ORDER BY Status;

-- 3. Fix any numeric status values that might exist in WorkflowInstances
PRINT '';
PRINT '3. FIXING WORKFLOW INSTANCE STATUS VALUES:';

-- Update any numeric status values to their string equivalents
UPDATE WorkflowInstances SET Status = 'InProgress' WHERE Status = '0' OR Status LIKE '[0-9]%';
UPDATE WorkflowInstances SET Status = 'Completed' WHERE Status = '1' OR Status = 'Complete';
UPDATE WorkflowInstances SET Status = 'Cancelled' WHERE Status = '2';
UPDATE WorkflowInstances SET Status = 'Failed' WHERE Status = '3';

PRINT 'Updated WorkflowInstances status values to string format';

-- 4. Fix any numeric status values that might exist in WorkflowStepInstances
PRINT '';
PRINT '4. FIXING WORKFLOW STEP INSTANCE STATUS VALUES:';

-- Update any numeric status values to their string equivalents
UPDATE WorkflowStepInstances SET Status = 'Pending' WHERE Status = '0' OR Status LIKE '[0-9]%';
UPDATE WorkflowStepInstances SET Status = 'InProgress' WHERE Status = '1';
UPDATE WorkflowStepInstances SET Status = 'Completed' WHERE Status = '2' OR Status = 'Complete';
UPDATE WorkflowStepInstances SET Status = 'Skipped' WHERE Status = '3';
UPDATE WorkflowStepInstances SET Status = 'Failed' WHERE Status = '4';

PRINT 'Updated WorkflowStepInstances status values to string format';

-- 5. Verify the fixes
PRINT '';
PRINT '5. VERIFICATION - WORKFLOW INSTANCE STATUS VALUES AFTER FIX:';
SELECT DISTINCT Status, COUNT(*) as Count
FROM WorkflowInstances
GROUP BY Status
ORDER BY Status;

PRINT '';
PRINT '6. VERIFICATION - WORKFLOW STEP INSTANCE STATUS VALUES AFTER FIX:';
SELECT DISTINCT Status, COUNT(*) as Count
FROM WorkflowStepInstances
GROUP BY Status
ORDER BY Status;

-- 7. Find any problematic workflow approvals query
PRINT '';
PRINT '7. TESTING WORKFLOW APPROVAL QUERY:';
BEGIN TRY
    SELECT TOP 5
        fr.Id,
        fr.FormDefinitionId,
        wsi.Status as StepStatus,
        ws.AssignedRoles
    FROM FormRequests fr
    INNER JOIN WorkflowInstances wi ON fr.WorkflowInstanceId = wi.Id
    INNER JOIN WorkflowStepInstances wsi ON wi.Id = wsi.WorkflowInstanceId
    INNER JOIN WorkflowSteps ws ON wsi.StepId = ws.StepId AND wi.WorkflowDefinitionId = ws.WorkflowDefinitionId
    WHERE wsi.Status = 'Pending';
    
    PRINT 'Workflow approval query executed successfully';
END TRY
BEGIN CATCH
    PRINT 'ERROR in workflow approval query: ' + ERROR_MESSAGE();
END CATCH

PRINT '';
PRINT 'STATUS DATA TYPE FIX COMPLETED';
