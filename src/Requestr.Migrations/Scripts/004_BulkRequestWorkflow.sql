-- Add Workflow Support to Bulk Requests Migration
-- This script adds workflow integration to the bulk request system
-- Migration: 004

USE Requestr;
GO

-- Add WorkflowInstanceId column to BulkFormRequests table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('BulkFormRequests') AND name = 'WorkflowInstanceId')
BEGIN
    ALTER TABLE BulkFormRequests
    ADD WorkflowInstanceId int NULL;
    
    -- Add foreign key constraint
    ALTER TABLE BulkFormRequests
    ADD CONSTRAINT FK_BulkFormRequests_WorkflowInstances 
    FOREIGN KEY (WorkflowInstanceId) REFERENCES WorkflowInstances(Id);
    
    -- Add index for better performance
    CREATE INDEX IX_BulkFormRequests_WorkflowInstanceId ON BulkFormRequests(WorkflowInstanceId);
    
    PRINT 'WorkflowInstanceId column added to BulkFormRequests table.';
END
GO

-- Add WorkflowFormRequestId column to BulkFormRequests table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('BulkFormRequests') AND name = 'WorkflowFormRequestId')
BEGIN
    ALTER TABLE BulkFormRequests
    ADD WorkflowFormRequestId int NULL;
    
    PRINT 'WorkflowFormRequestId column added to BulkFormRequests table.';
END
GO

PRINT 'Bulk request workflow migration completed successfully.';
GO
