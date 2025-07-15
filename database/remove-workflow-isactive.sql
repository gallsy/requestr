-- Remove IsActive functionality from WorkflowDefinitions
-- This script removes the IsActive column and related indexes from WorkflowDefinitions table
-- Run this after code changes have been deployed

USE RequestrApp;
GO

PRINT 'Starting removal of IsActive functionality from WorkflowDefinitions...'

-- Drop indexes that reference IsActive
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowDefinitions_IsActive' AND object_id = OBJECT_ID('WorkflowDefinitions'))
BEGIN
    DROP INDEX IX_WorkflowDefinitions_IsActive ON WorkflowDefinitions;
    PRINT 'Dropped index IX_WorkflowDefinitions_IsActive';
END

-- Drop composite indexes that include IsActive
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowDefinitions_FormDefinitionId_IsActive' AND object_id = OBJECT_ID('WorkflowDefinitions'))
BEGIN
    DROP INDEX IX_WorkflowDefinitions_FormDefinitionId_IsActive ON WorkflowDefinitions;
    PRINT 'Dropped index IX_WorkflowDefinitions_FormDefinitionId_IsActive';
END

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include' AND object_id = OBJECT_ID('WorkflowDefinitions'))
BEGIN
    DROP INDEX IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include ON WorkflowDefinitions;
    PRINT 'Dropped index IX_WorkflowDefinitions_FormDefinitionId_IsActive_Include';
END

-- Remove the IsActive column
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('WorkflowDefinitions') AND name = 'IsActive')
BEGIN
    ALTER TABLE WorkflowDefinitions DROP COLUMN IsActive;
    PRINT 'Removed IsActive column from WorkflowDefinitions table';
END
ELSE
BEGIN
    PRINT 'IsActive column does not exist in WorkflowDefinitions table';
END

-- Recreate FormDefinitionId index without IsActive
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_WorkflowDefinitions_FormDefinitionId' AND object_id = OBJECT_ID('WorkflowDefinitions'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_WorkflowDefinitions_FormDefinitionId 
    ON WorkflowDefinitions (FormDefinitionId);
    PRINT 'Created index IX_WorkflowDefinitions_FormDefinitionId';
END

PRINT 'Successfully removed IsActive functionality from WorkflowDefinitions table.'
GO
